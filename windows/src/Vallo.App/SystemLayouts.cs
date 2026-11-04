using Vallo.Core;
using Vallo.Platform;
using static Vallo.App.Native;

namespace Vallo.App;

/// Раскладки системы: какие HKL у EN (0x0409) и RU (0x0419), их таблицы, что стоит сейчас.
/// Таблицы меняет только поток слежения за раскладкой; HKL читают все — поля volatile.
sealed unsafe class SystemLayouts
{
    const ushort LangEn = 0x0409, LangRu = 0x0419;

    volatile nint enHkl, ruHkl;
    public LayoutPair Pair { get; private set; }

    SystemLayouts(nint en, nint ru)
    {
        enHkl = en;
        ruHkl = ru;
        Pair = new LayoutPair(Table(Language.En, en), Table(Language.Ru, ru));
    }

    /// null — в системе нет английской (США) или русской раскладки: свитчеру не из чего переводить.
    public static SystemLayouts? Create()
    {
        nint en = 0, ru = 0;
        // стоящая сейчас раскладка — в приоритете (вдруг у человека две английские)
        var current = Current();
        if (LanguageOf(current) == Language.En) en = current;
        if (LanguageOf(current) == Language.Ru) ru = current;
        foreach (var hkl in Installed())
        {
            if (en == 0 && LanguageOf(hkl) == Language.En) en = hkl;
            if (ru == 0 && LanguageOf(hkl) == Language.Ru) ru = hkl;
        }
        return en != 0 && ru != 0 ? new SystemLayouts(en, ru) : null;
    }

    public nint Hkl(Language l) => l == Language.En ? enHkl : ruHkl;

    /// Язык по младшему слову HKL; null — третья раскладка.
    public static Language? LanguageOf(nint hkl) => (ushort)(hkl & 0xFFFF) switch
    {
        LangEn => Language.En,
        LangRu => Language.Ru,
        _ => null,
    };

    /// Раскладка активного окна; 0 — активного окна нет (экран блокировки и т. п.).
    public static nint Current()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return 0;
        var tid = GetWindowThreadProcessId(hwnd, null);
        return tid == 0 ? 0 : GetKeyboardLayout(tid);
    }

    /// Человек выбрал другую раскладку того же языка: снять её таблицу. null — уже она и стоит.
    public LayoutPair? Adopt(Language l, nint hkl)
    {
        if (hkl == Hkl(l)) return null;
        var table = Table(l, hkl);
        if (l == Language.En) enHkl = hkl; else ruHkl = hkl;
        Pair = l == Language.En ? Pair with { En = table } : Pair with { Ru = table };
        return Pair;
    }

    static List<nint> Installed()
    {
        var n = GetKeyboardLayoutList(0, null);
        if (n <= 0) return [];
        var buf = new nint[n];
        fixed (nint* p = buf) n = GetKeyboardLayoutList(n, p);
        return buf.Take(Math.Max(n, 0)).ToList();
    }

    /// Та же таблица, что снимает DumpLayouts, — общий LayoutReader.
    static TableKeyLayout Table(Language l, nint hkl)
    {
        var table = new Dictionary<KeyStroke, string>();
        foreach (var e in LayoutReader.Read(hkl))
            table[new KeyStroke((ushort)e.KeyCode, e.Shift, e.CapsLock)] = e.Char;
        return new TableKeyLayout(l, table);
    }
}
