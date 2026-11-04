using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

/// Общие примеры поведения свитчера: Data/tests/switcher-cases.tsv (формат — в Data/tests/README.md).
/// Тот же файл прогоняет Мак (Tests/ValloCoreTests/SharedCasesTests.swift); упавший пример — ошибка порта.
public class SharedCasesTests
{
    public static IEnumerable<object[]> Cases()
    {
        var lines = File.ReadAllText(TestSupport.RepoData("tests/switcher-cases.tsv")).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
            if (line.Trim(' ', '\t').Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split('\t');
            if (f.Length < 3) throw new InvalidDataException($"строка {i + 1}: ждём минимум 3 поля через табуляцию");
            if (f[0] != "en" && f[0] != "ru") throw new InvalidDataException($"строка {i + 1}: start должен быть en или ru");
            yield return new object[] { i + 1, f[0], f[1], Unescape(f[2]), f.Length > 3 ? f[3] : "" };
        }
    }

    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

    // полные словари, как в приложении: ru.txt с добавкой ru-modern.txt, выученных нет
    static readonly Lexicon FullEN = Lexicon.Load(TestSupport.RepoData("wordlists/en.txt"), Language.En);
    static readonly Lexicon FullRU = Lexicon.Load(TestSupport.RepoData("wordlists/ru.txt"), Language.Ru,
        extra: WordForms.Load(TestSupport.RepoData("wordlists/ru-modern.txt")));

    const ushort Backspace = 0x08, Enter = 0x0D, Tab = 0x09, Esc = 0x1B;

    enum Kind { Key, DoubleShift, Backspace, Enter, Tab, Esc }

    static List<(Kind Kind, char Ch)> Tokens(string keys)
    {
        var known = new Dictionary<string, Kind>
        {
            ["⇧⇧"] = Kind.DoubleShift, ["⌫"] = Kind.Backspace, ["⏎"] = Kind.Enter, ["⇥"] = Kind.Tab, ["ESC"] = Kind.Esc,
        };
        var result = new List<(Kind, char)>();
        var i = 0;
        while (i < keys.Length)
        {
            var close = keys[i] == '{' ? keys.IndexOf('}', i) : -1;
            if (close > 0 && known.TryGetValue(keys[(i + 1)..close], out var t))
            {
                result.Add((t, '\0'));
                i = close + 1;
            }
            else
            {
                result.Add((Kind.Key, keys[i]));   // `{` вне известного токена — обычная клавиша (Shift+[)
                i++;
            }
        }
        return result;
    }

    /// Экран-симулятор: что окажется в поле ввода.
    /// delete(n) по README считает воспринимаемые символы; здесь — единицы UTF-16: в файле примеров
    /// нет ни суррогатных пар, ни составных символов, так что это одно и то же.
    static string Simulate(Language start, string keys)
    {
        var pair = TestSupport.FixturePair();
        var engine = new Engine(pair, new Judge(FullEN, FullRU, new HashSet<string>()), start);
        var shown = start;
        var screen = "";

        void Apply(IEnumerable<EngineAction> actions)
        {
            foreach (var a in actions)
                switch (a)
                {
                    case EngineAction.Delete d: screen = screen[..^Math.Min(d.N, screen.Length)]; break;
                    case EngineAction.Insert ins: screen += ins.Text; break;
                    case EngineAction.SwitchLayout s: shown = s.L; break;
                    case EngineAction.PressKey p: screen += p.Vk == Enter ? "\n" : p.Vk == Tab ? "\t" : ""; break;
                }
        }

        void Press(KeyStroke stroke)
        {
            var output = engine.HandleKey(new KeyEvent(stroke, false));
            if (output is EngineOutput.Consume c) { Apply(c.Actions); return; }
            if (stroke.KeyCode == Backspace) { if (screen.Length > 0) screen = screen[..^1]; }
            else if (stroke.KeyCode == Enter) screen += "\n";
            else if (stroke.KeyCode == Tab) screen += "\t";
            else if (stroke.KeyCode != Esc) screen += pair.Layout(shown).Character(stroke) ?? "";
        }

        foreach (var (kind, ch) in Tokens(keys))
            switch (kind)
            {
                case Kind.Key: Press(TestSupport.StrokesTyping(ch.ToString(), (TableKeyLayout)pair.En)[0]); break;
                case Kind.DoubleShift: Apply(engine.HandleDoubleShift(null)); break;
                case Kind.Backspace: Press(new KeyStroke(Backspace)); break;
                case Kind.Enter: Press(new KeyStroke(Enter)); break;
                case Kind.Tab: Press(new KeyStroke(Tab)); break;
                case Kind.Esc: Press(new KeyStroke(Esc)); break;
            }
        return screen;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedCase(int line, string start, string keys, string screen, string note)
    {
        var got = Simulate(start == "ru" ? Language.Ru : Language.En, keys);
        Assert.True(got == screen, $"строка {line} [{note}]: keys={keys} ожидали «{screen}», получили «{got}»");
    }
}
