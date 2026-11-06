namespace Vallo.Core;

public readonly record struct KeyEvent(KeyStroke Stroke, bool HasCtrlOrAlt);

/// Что сделать «рукам» приложения.
public abstract record EngineAction
{
    public sealed record Delete(int N) : EngineAction;
    public sealed record Insert(string Text) : EngineAction;
    public sealed record SwitchLayout(Language L) : EngineAction;
    public sealed record ReplaceSelection(string Text) : EngineAction;
    public sealed record Learn(string Word) : EngineAction;
    /// Нажать клавишу (Enter или Tab, которые мы проглотили, чтобы сначала исправить слово).
    public sealed record PressKey(ushort Vk) : EngineAction;
}

public abstract record EngineOutput
{
    /// Нажатие уходит в приложение как есть.
    public static readonly EngineOutput PassThrough = new PassThroughOutput();

    sealed record PassThroughOutput : EngineOutput
    {
        public override string ToString() => "PassThrough";
    }

    /// Нажатие проглатываем и делаем действия (они уже включают проглоченную клавишу).
    public sealed record Consume(IReadOnlyList<EngineAction> Actions) : EngineOutput
    {
        // списки сравниваем по содержимому, а не по ссылке
        public bool Equals(Consume? other) => other is not null && Actions.SequenceEqual(other.Actions);
        public override int GetHashCode() => Actions.Aggregate(0, (h, a) => HashCode.Combine(h, a));
        public override string ToString() => $"Consume [{string.Join(", ", Actions)}]";
    }
}

/// Движок: память набора + решения судьи → действия. Без системных API.
public sealed class Engine(LayoutPair layouts, Judge judge, Language current)
{
    public const int BufferLimit = 200;
    /// Esc, стрелки, Home, End, PgUp, PgDn, Delete.
    static readonly HashSet<ushort> ResetKeys = [0x1B, 0x25, 0x26, 0x27, 0x28, 0x24, 0x23, 0x21, 0x22, 0x2E];
    /// Enter (и на цифровом блоке — тот же VK), Tab: сначала проверяем слово, потом сбрасываем память.
    static readonly HashSet<ushort> LineEndKeys = [0x0D, 0x09];
    public const ushort BackspaceKey = 0x08;

    public bool AutoSwitchEnabled { get; set; } = true;
    public Judge Judge { get; set; } = judge;
    public Language CurrentLanguage { get; private set; } = current;
    public string? LastDecision { get; private set; }

    LayoutPair layouts = layouts;

    /// Нажатие и раскладка, в которой оно сейчас показано на экране.
    sealed class Entry(KeyStroke stroke, Language language)
    {
        public KeyStroke Stroke = stroke;
        public Language Language = language;
    }

    readonly List<Entry> buffer = new();
    /// Начало текущего слова в буфере.
    int wordStart;
    /// Текущее слово автоисправлено (из какого языка).
    Language? autoSwitchedFrom;
    /// В текущем слове автопереключение больше не трогаем.
    bool wordLocked;
    /// Стоит третья раскладка (не RU и не EN) — молчим, пока не вернут RU/EN.
    bool foreignLayout;

    /// Отменяемое автоисправление: слово с `Start` и всё после него (граница), из языка `From`.
    sealed record Undo(int Start, Language From);
    Undo? undo;

    /// Короткое слово (1–2 буквы) — самое ненадёжное решение. Если следующее слово ушло в язык `Candidate`,
    /// короткое тоже переводим в него.
    sealed record ShortSwitch(int Start, int NextWordStart, Language Candidate);
    ShortSwitch? shortSwitch;

    /// Слова, откаченные один раз. Запоминаем навсегда только со второго отката того же слова.
    readonly HashSet<string> undoneOnce = new();

    /// В памяти набора что-то есть — тогда свежего выделения быть не может.
    public bool HasTypedText => buffer.Count > 0;

    /// Двойной Shift сейчас откатит автоисправление (тогда выделение не читаем).
    public bool CanUndo => undo != null;

    /// Третья раскладка (не RU и не EN) — движок молчит.
    public bool IsForeignLayout => foreignLayout;

    // ---- Нажатия

    public EngineOutput HandleKey(KeyEvent e)
    {
        var stroke = e.Stroke;
        if (foreignLayout) return EngineOutput.PassThrough;
        if (e.HasCtrlOrAlt || ResetKeys.Contains(stroke.KeyCode))
        {
            Reset();
            return EngineOutput.PassThrough;
        }
        if (LineEndKeys.Contains(stroke.KeyCode)) return HandleLineEnd(stroke.KeyCode);
        if (stroke.KeyCode == BackspaceKey)
        {
            HandleBackspace();
            return EngineOutput.PassThrough;
        }
        if (layouts.En.Character(stroke) == null && layouts.Ru.Character(stroke) == null)
        {
            Reset();
            return EngineOutput.PassThrough;
        }
        if (buffer.Count >= BufferLimit) Reset();
        return IsBoundary(stroke) ? HandleBoundary(stroke) : HandleLetter(stroke);
    }

    EngineOutput HandleLetter(KeyStroke stroke)
    {
        // первая буква нового слова: откат прошлого больше не доступен, блокировка прошлого слова снята
        if (wordStart == buffer.Count)
        {
            undo = null;
            StartNewWord();
            if (shortSwitch is { } s && s.NextWordStart != wordStart) shortSwitch = null;
        }
        var previous = Display(wordStart, buffer.Count).Length;
        buffer.Add(new Entry(stroke, CurrentLanguage));

        if (!AutoSwitchEnabled || wordLocked) return EngineOutput.PassThrough;
        var word = Strokes(wordStart, buffer.Count);

        if (autoSwitchedFrom is { } from)
        {
            var original = Converter.Render(word, layouts.Layout(from));
            var converted = Converter.Render(word, layouts.Layout(CurrentLanguage));
            if (!Judge.ShouldRevert(original, converted, from)) return EngineOutput.PassThrough;
            LastDecision = $"«{converted}» не похоже на слово — вернул «{original}»";
            SetWordLanguage(from);
            wordLocked = true;
            undo = null;
            return Out(new EngineAction.Delete(previous), new EngineAction.Insert(original), new EngineAction.SwitchLayout(from));
        }

        var current = Converter.Render(word, layouts.Layout(CurrentLanguage));
        var alternative = Converter.Render(word, layouts.Layout(CurrentLanguage.Other()));
        if (Judge.MidWord(current, alternative, CurrentLanguage) is not Verdict.SwitchLanguage sw)
            return EngineOutput.PassThrough;
        LastDecision = sw.Reason;
        var source = CurrentLanguage;
        autoSwitchedFrom = source;
        if (RevertShortSwitch(source.Other(), alternative) is { } revert) return revert;
        undo = new Undo(wordStart, source);
        SetWordLanguage(source.Other());
        return Out(new EngineAction.Delete(previous), new EngineAction.Insert(alternative), new EngineAction.SwitchLayout(source.Other()));
    }

    /// Текущее слово переключаем в `target`, а прошлое короткое было исправлено как раз из `target` —
    /// значит, оно ошибка: стираем оба и печатаем оба в `target`. `word` — текущее слово уже в `target`.
    EngineOutput? RevertShortSwitch(Language target, string word, int? end = null)
    {
        if (shortSwitch is not { } s || s.NextWordStart != wordStart || s.Candidate != target) return null;
        shortSwitch = null;
        var shownEnd = end ?? buffer.Count - 1;   // новая буква ещё не на экране
        var shown = Display(s.Start, shownEnd);
        var shortPart = Converter.Render(Strokes(s.Start, wordStart), layouts.Layout(target));
        for (var i = s.Start; i < buffer.Count; i++) buffer[i].Language = target;
        CurrentLanguage = target;
        undo = null;
        LastDecision = $"«{shortPart.Trim(' ', '\t')}» исправил зря — следующее слово тоже {target.Code().ToUpperInvariant()}";
        return Out(new EngineAction.Delete(shown.Length), new EngineAction.Insert(shortPart + word), new EngineAction.SwitchLayout(target));
    }

    EngineOutput HandleBoundary(KeyStroke stroke)
    {
        try
        {
            var word = Strokes(wordStart, buffer.Count);
            // слово уже в своей раскладке, но это аббревиатура с заглавной («Pdf») — только поднять регистр
            if (AutoSwitchEnabled && !wordLocked && word.Count > 0)
            {
                var shown = Display(wordStart, buffer.Count);
                if (CapitalizedAbbreviation(wordStart, buffer.Count) is { } upper)
                {
                    LastDecision = $"«{shown}» — аббревиатура, пишем заглавными";
                    buffer.Add(new Entry(stroke, CurrentLanguage));
                    var text = layouts.Layout(CurrentLanguage).Character(stroke) ?? "";
                    return Out(new EngineAction.Delete(shown.Length), new EngineAction.Insert(upper + text));
                }
            }
            if (!AutoSwitchEnabled || wordLocked || autoSwitchedFrom != null || word.Count == 0)
            {
                buffer.Add(new Entry(stroke, CurrentLanguage));
                return EngineOutput.PassThrough;
            }
            var current = Converter.Render(word, layouts.Layout(CurrentLanguage));
            var alternative = Converter.Render(word, layouts.Layout(CurrentLanguage.Other()));
            if (IsUnitAfterNumber(current))
            {
                buffer.Add(new Entry(stroke, CurrentLanguage));
                return EngineOutput.PassThrough;
            }
            if (Judge.WordEnd(current, alternative, CurrentLanguage) is not Verdict.SwitchLanguage sw)
            {
                // короткое слово оставили, но его можно прочитать как слово другого языка — решит следующее слово
                var start = wordStart;
                buffer.Add(new Entry(stroke, CurrentLanguage));
                if (Judge.IsShortCandidate(alternative, CurrentLanguage.Other()))
                    shortSwitch = new ShortSwitch(start, buffer.Count, CurrentLanguage.Other());
                return EngineOutput.PassThrough;
            }
            LastDecision = sw.Reason;
            var from = CurrentLanguage;
            var boundaryText = layouts.Layout(from.Other()).Character(stroke) ?? "";
            buffer.Add(new Entry(stroke, from.Other()));
            if (RevertShortSwitch(from.Other(), alternative + boundaryText, buffer.Count - 1) is { } revert)
                return revert;
            var isShort = Judge.IsShort(current, alternative);
            undo = new Undo(wordStart, from);
            SetWordLanguage(from.Other());
            if (isShort) shortSwitch = new ShortSwitch(wordStart, buffer.Count, from);
            var fixedWord = CapitalizedAbbreviation(wordStart, buffer.Count - 1) ?? alternative;
            return Out(new EngineAction.Delete(current.Length), new EngineAction.Insert(fixedWord + boundaryText),
                new EngineAction.SwitchLayout(CurrentLanguage));
        }
        finally { StartNewWord(); }
    }

    /// Enter и Tab: как граница слова, но после них память набора сбрасывается.
    EngineOutput HandleLineEnd(ushort keyCode)
    {
        try
        {
            var word = Strokes(wordStart, buffer.Count);
            if (!AutoSwitchEnabled || wordLocked || autoSwitchedFrom != null || word.Count == 0)
                return EngineOutput.PassThrough;
            var current = Converter.Render(word, layouts.Layout(CurrentLanguage));
            var alternative = Converter.Render(word, layouts.Layout(CurrentLanguage.Other()));
            if (IsUnitAfterNumber(current)
                || Judge.WordEnd(current, alternative, CurrentLanguage) is not Verdict.SwitchLanguage sw)
                return EngineOutput.PassThrough;
            LastDecision = sw.Reason;
            CurrentLanguage = CurrentLanguage.Other();
            return Out(new EngineAction.Delete(current.Length), new EngineAction.Insert(alternative),
                new EngineAction.SwitchLayout(CurrentLanguage), new EngineAction.PressKey(keyCode));
        }
        finally { Reset(); }
    }

    void HandleBackspace()
    {
        if (buffer.Count == 0) return;
        buffer.RemoveAt(buffer.Count - 1);
        undo = null;
        shortSwitch = null;
        if (buffer.Count < wordStart)
        {
            // стёрли границу и залезли в прошлое слово — его больше не трогаем
            wordStart = buffer.FindLastIndex(x => IsBoundary(x.Stroke)) + 1;
            wordLocked = true;
        }
    }

    // ---- Двойной Shift

    public IReadOnlyList<EngineAction> HandleDoubleShift(string? selection)
    {
        if (foreignLayout) return [];
        if (undo is { } u)
        {
            var shown = Display(u.Start, buffer.Count);
            var original = Converter.Render(Strokes(u.Start, buffer.Count), layouts.Layout(u.From));
            var word = Lexicon.Normalize(Judge.Core(original));
            for (var i = u.Start; i < buffer.Count; i++) buffer[i].Language = u.From;
            CurrentLanguage = u.From;
            wordLocked = true;
            undo = null;
            var actions = new List<EngineAction>
            {
                new EngineAction.Delete(shown.Length), new EngineAction.Insert(original), new EngineAction.SwitchLayout(u.From),
            };
            if (undoneOnce.Contains(word))
            {
                Judge.Learned.Add(word);
                undoneOnce.Remove(word);
                actions.Add(new EngineAction.Learn(word));
                LastDecision = $"откат: «{shown}» → «{original}», второй раз — запомнил «{word}»";
            }
            else
            {
                undoneOnce.Add(word);
                LastDecision = $"откат: «{shown}» → «{original}», запомню, если откатишь ещё раз";
            }
            return actions;
        }

        if (!string.IsNullOrEmpty(selection))
        {
            var cyrillic = selection.Count(c => Lexicon.IsInAlphabet(char.ToLowerInvariant(c), Language.Ru));
            var latin = selection.Count(c => Lexicon.IsInAlphabet(char.ToLowerInvariant(c), Language.En));
            var from = cyrillic > latin ? Language.Ru : Language.En;
            var converted = Converter.Convert(selection, layouts.Layout(from), layouts.Layout(from.Other()));
            // переводить нечего (числа, ячейка Excel) — не вставляем то же самое и раскладку не трогаем
            if (converted == selection) return [];
            Reset();
            CurrentLanguage = from.Other();
            return [new EngineAction.ReplaceSelection(converted), new EngineAction.SwitchLayout(from.Other())];
        }

        if (buffer.Count == 0) return [];
        var target = CurrentLanguage.Other();
        var all = Display(0, buffer.Count);
        var text = Converter.Render(Strokes(0, buffer.Count), layouts.Layout(target));
        foreach (var x in buffer) x.Language = target;
        CurrentLanguage = target;
        wordLocked = true;
        LastDecision = $"двойной Shift: всё набранное → {target.Code().ToUpperInvariant()}";
        return [new EngineAction.Delete(all.Length), new EngineAction.Insert(text), new EngineAction.SwitchLayout(target)];
    }

    // ---- Сбросы

    /// Клик мышью, смена приложения, навигация.
    public void Reset()
    {
        buffer.Clear();
        undo = null;
        shortSwitch = null;
        StartNewWord();
    }

    /// Человек выбрал другую раскладку того же языка — новые таблицы.
    public void UpdateLayouts(LayoutPair pair)
    {
        layouts = pair;
        Reset();
    }

    /// Раскладку сменили не мы. Та же, что уже стоит, — эхо нашего же переключения, память не трогаем.
    /// null — третья раскладка (не RU и не EN): сброс и молчание до возврата RU/EN.
    public void ExternalLayoutChange(Language? language)
    {
        if (language is not { } l)
        {
            Reset();
            foreignLayout = true;
            return;
        }
        if (foreignLayout)
        {
            foreignLayout = false;
            Reset();
            CurrentLanguage = l;
            return;
        }
        if (l == CurrentLanguage) return;
        Reset();
        CurrentLanguage = l;
    }

    // ---- Помощники

    static EngineOutput Out(params EngineAction[] actions) => new EngineOutput.Consume(actions);

    List<KeyStroke> Strokes(int start, int end) => buffer.GetRange(start, end - start).Select(x => x.Stroke).ToList();

    /// Известная аббревиатура хоть с одной заглавной («Pdf», «Жкх») — целиком заглавными.
    /// Меняет нажатия в буфере на заглавные и возвращает новый вид слова; иначе null.
    string? CapitalizedAbbreviation(int start, int end)
    {
        var shown = Display(start, end);
        if (start >= end) return null;
        var language = buffer[start].Language;
        if (!Judge.IsKnownAbbreviation(shown, language) || !shown.Any(char.IsUpper) || Judge.IsAbbreviation(shown))
            return null;
        for (var i = start; i < end; i++) buffer[i].Stroke = new KeyStroke(buffer[i].Stroke.KeyCode, true);
        return Display(start, end);
    }

    void StartNewWord()
    {
        wordStart = buffer.Count;
        autoSwitchedFrom = null;
        wordLocked = false;
    }

    void SetWordLanguage(Language language)
    {
        for (var i = wordStart; i < buffer.Count; i++) buffer[i].Language = language;
        CurrentLanguage = language;
    }

    /// Как кусок буфера сейчас выглядит на экране.
    /// Как кусок буфера выглядит на экране. Мёртвые клавиши печатают вместе со следующей, поэтому считаем с начала
    /// буфера и отрезаем то, что до `start`.
    string Display(int start, int end)
    {
        var all = Shown(end);
        var before = Shown(start);
        return all.StartsWith(before, StringComparison.Ordinal) ? all[before.Length..] : all[Math.Min(before.Length, all.Length)..];
    }

    /// Первые `count` нажатий буфера на экране: подряд идущие в одной раскладке — через Converter.Render.
    string Shown(int count)
    {
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < count)
        {
            var language = buffer[i].Language;
            var j = i;
            while (j < count && buffer[j].Language == language) j++;
            sb.Append(Converter.Render(Strokes(i, j), layouts.Layout(language)));
            i = j;
        }
        return sb.ToString();
    }

    /// «руб», «шт» сразу после числа («850руб», «5 шт», «2 млн руб») — единица, не ошибка раскладки.
    bool IsUnitAfterNumber(string current)
    {
        if (!Judge.IsUnitAfterNumber(current, CurrentLanguage)) return false;
        var before = new string(Display(0, wordStart).Reverse().SkipWhile(c => c == ' ' || c == '.').ToArray());
        if (before.Length > 0 && char.IsNumber(before[0])) return true;
        var previous = new string(before.TakeWhile(char.IsLetter).Reverse().ToArray());
        return SwitcherRules.Shared.UnitsBeforeUnit.Contains(previous.ToLowerInvariant());
    }

    /// Граница слова — клавиша, которая ни в одной раскладке не даёт букву.
    bool IsBoundary(KeyStroke stroke)
    {
        static bool IsLetter(string? s) => !string.IsNullOrEmpty(s) && char.IsLetter(s[0]);
        return !IsLetter(layouts.En.Character(stroke)) && !IsLetter(layouts.Ru.Character(stroke));
    }
}
