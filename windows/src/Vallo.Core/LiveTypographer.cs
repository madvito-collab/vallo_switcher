namespace Vallo.Core;

/// Правка на экране: стереть `Delete` символов перед курсором (как их видит человек) и вставить `Insert`.
public readonly record struct TypoEdit(int Delete, string Insert);

/// Что делать на лету: типографика, опечатки, «ё», смайлики — каждое включается отдельно (null — выключено).
public sealed record LiveOptions(bool Typography, EmojiBrackets? Emoji = null, TypoCorrector? Typos = null, Yoficator? Yo = null)
{
    /// Хоть что-то включено — свитчеру есть смысл следить за набором.
    public bool Any => Typography || Emoji is not null || Typos is not null || Yo is not null;
}

/// Типографика на лету: помнит последние набранные символы, на каждый новый прогоняет `Typographer.Format`
/// (`final: false`) и превращает разницу в правку. Backspace сразу после правки возвращает набранное.
/// Порт маковского `LiveTypographer.swift`; символы считаем как человек (смайлик — один).
public sealed class LiveTypographer
{
    public const int ContextLimit = 60;

    /// Текст на экране с последнего сброса (последние `ContextLimit` символов).
    List<string> context = [];
    /// Что слева от контекста, неизвестно: правила начала строки и слова не срабатывают.
    bool leftUnknown = true;
    /// Контекст начинается посреди слова (свитчер исправил раскладку посреди слова) — первое слово не исправляем.
    bool startsMidWord;
    /// Столько первых символов контекста не трогаем: человек отменил правку — значит, хотел именно так.
    int frozen;
    /// Последняя правка: что показали и что человек набрал на самом деле.
    (List<string> Shown, List<string> Typed, string? TypoWord)? undo;

    /// После `Backspace()`: слово, исправление которого человек отменил (нормализованное) — в «Не исправлять слова».
    public string? UndoneTypoWord { get; private set; }

    /// Длина текста на экране изменилась (… вместо ...) — свитчеру надо сбросить память.
    public bool LastEditChangedLength { get; private set; }

    /// Человек набрал символ. null — пропустить символ как есть.
    public TypoEdit? Typed(string ch, Language hint, LiveOptions options)
    {
        undo = null;
        UndoneTypoWord = null;
        LastEditChangedLength = false;
        var raw = new List<string>(context) { ch };
        // сначала опечатка в только что законченном слове, потом типографика
        var (corrected, typoWord) = FixTypo(raw, options);
        // смайлики — до типографики: она чистит пробелы у скобок
        if (options.Emoji?.Apply(corrected, frozen) is { } withEmoji) corrected = withEmoji;
        List<string> Style(List<string> chars, bool unknown) => options.Typography ? Format(chars, unknown, hint) : chars;
        var formatted = Style(corrected, leftUnknown);
        var p = CommonPrefix(raw, formatted);
        if (p < frozen)
        {
            // правка задела отменённое место — его не трогаем, а остальное причёсываем как обычно
            formatted = [.. raw.GetRange(0, frozen), .. Style(corrected.GetRange(frozen, corrected.Count - frozen), true)];
            p = CommonPrefix(raw, formatted);
        }
        if (formatted.SequenceEqual(raw))
        {
            SetContext(raw);
            return null;
        }
        var edit = new TypoEdit(context.Count - p, string.Concat(formatted.Skip(p)));
        undo = (formatted.Skip(p).ToList(), raw.Skip(p).ToList(), typoWord);
        LastEditChangedLength = formatted.Count != raw.Count;
        SetContext(formatted);
        return edit;
    }

    /// Свитчер исправил раскладку слова на его конце: стёр `delete` символов и напечатал `insert` (слово + граница).
    /// Причёсываем так, будто человек набрал исправленное («850he,» + пробел → «850руб » → «850 ₽ »).
    /// Правка — относительно экрана уже после вставки. null — править нечего или слово в памяти не целиком.
    public TypoEdit? Replaced(int delete, string insert, Language hint, LiveOptions options)
    {
        var inserted = TextElements.Split(insert);
        if (inserted.Count == 0 || delete > context.Count - frozen)
        {
            Reset(midWord: false);
            return null;
        }
        context = [.. context.Take(context.Count - delete), .. inserted.Take(inserted.Count - 1)];
        if (Typed(inserted[^1], hint, options) is not { } edit) return null;
        // граница уже на экране — её тоже стираем
        return new TypoEdit(edit.Delete + 1, edit.Insert);
    }

    /// Backspace. Сразу после правки — правка-отмена (вернуть набранное), иначе null (обычный Backspace).
    public TypoEdit? Backspace()
    {
        if (undo is not { } u)
        {
            if (context.Count > 0) context.RemoveAt(context.Count - 1);
            frozen = Math.Min(frozen, context.Count);
            return null;
        }
        undo = null;
        UndoneTypoWord = u.TypoWord;
        LastEditChangedLength = u.Shown.Count != u.Typed.Count;
        context = [.. context.Take(context.Count - u.Shown.Count), .. u.Typed];
        frozen = context.Count;
        return new TypoEdit(u.Shown.Count, string.Concat(u.Typed));
    }

    /// `knownLineStart` — перед контекстом точно начало строки (для тестов и пустого поля).
    /// `midWord` — сброс посреди слова (свитчер исправил раскладку): в памяти дальше будет хвост слова.
    public void Reset(bool knownLineStart = false, bool midWord = false)
    {
        leftUnknown = !knownLineStart;
        startsMidWord = midWord;
        context = [];
        frozen = 0;
        undo = null;
        LastEditChangedLength = false;
    }

    void SetContext(List<string> chars)
    {
        var extra = Math.Max(0, chars.Count - ContextLimit);
        context = chars.Skip(extra).ToList();
        frozen = Math.Max(0, frozen - extra);
        if (extra > 0) { leftUnknown = true; startsMidWord = true; }
    }

    static bool IsLetter(string s) => s.Length > 0 && char.IsLetter(s, 0);

    /// Набран символ-граница (не буква) — исправить опечатку в слове перед ним, поставить «ё», дефис частицы.
    /// Отменённое место (`frozen`) не трогаем. Возвращает текст и исходное слово, если в нём была опечатка
    /// (только «ё» — не ошибка, такое слово в «Не исправлять слова» при отмене не попадает).
    (List<string> Text, string? TypoWord) FixTypo(List<string> raw, LiveOptions options)
    {
        if ((options.Typos is null && options.Yo is null) || raw.Count == 0 || IsLetter(raw[^1])) return (raw, null);
        var boundary = raw[^1];
        var bodyCount = raw.Count - 1;
        var start = bodyCount;
        while (start > 0 && IsLetter(raw[start - 1])) start--;
        if (start < frozen || start >= bodyCount) return (raw, null);
        // контекст начался посреди слова — это хвост, не трогаем
        if (start == 0 && startsMidWord) return (raw, null);
        var word = string.Concat(raw.GetRange(start, bodyCount - start));
        string? typoWord = null;
        var fixedWord = word;
        if (options.Typos?.Correct(word) is { } corrected)
        {
            fixedWord = corrected;
            typoWord = Lexicon.Normalize(word);
        }
        // «ё» — после опечатки: «далекйи» → «далекий» → «далёкий»
        if (options.Yo?.Fix(fixedWord) is { } withYo) fixedWord = withYo;
        List<string> result = [.. raw.GetRange(0, start), .. TextElements.Split(fixedWord), boundary];
        if (options.Typos is not { } typos) return (result, typoWord);
        var joined = TextElements.Split(typos.JoinParticles(string.Concat(result), live: true));
        return (joined.Count == result.Count ? joined : result, typoWord);
    }

    static int CommonPrefix(List<string> a, List<string> b)
    {
        var n = 0;
        while (n < a.Count && n < b.Count && a[n] == b[n]) n++;
        return n;
    }

    static List<string> Format(List<string> chars, bool unknownLeft, Language hint)
    {
        var input = (unknownLeft ? Typographer.UnknownLeft.ToString() : "") + string.Concat(chars);
        var output = Typographer.Format(input, final: false, hint: hint);
        if (unknownLeft && output.Length > 0 && output[0] == Typographer.UnknownLeft) output = output[1..];
        return TextElements.Split(output);
    }
}
