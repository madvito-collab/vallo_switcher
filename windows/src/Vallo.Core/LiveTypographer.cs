namespace Vallo.Core;

/// Правка на экране: стереть `Delete` символов перед курсором (как их видит человек) и вставить `Insert`.
public readonly record struct TypoEdit(int Delete, string Insert);

/// Что делать на лету: типографика и смайлики (опечатки и «ё» на Windows — следующая очередь).
public sealed record LiveOptions(bool Typography, EmojiBrackets? Emoji = null);

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
    /// Столько первых символов контекста не трогаем: человек отменил правку — значит, хотел именно так.
    int frozen;
    /// Последняя правка: что показали и что человек набрал на самом деле.
    (List<string> Shown, List<string> Typed)? undo;

    /// Длина текста на экране изменилась (… вместо ...) — свитчеру надо сбросить память.
    public bool LastEditChangedLength { get; private set; }

    /// Человек набрал символ. null — пропустить символ как есть.
    public TypoEdit? Typed(string ch, Language hint, LiveOptions options)
    {
        undo = null;
        LastEditChangedLength = false;
        var raw = new List<string>(context) { ch };
        var corrected = raw;
        // смайлики — до типографики: она чистит пробелы у скобок
        if (options.Emoji?.Apply(raw, frozen) is { } withEmoji) corrected = withEmoji;
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
        undo = (formatted.Skip(p).ToList(), raw.Skip(p).ToList());
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
            Reset();
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
        LastEditChangedLength = u.Shown.Count != u.Typed.Count;
        context = [.. context.Take(context.Count - u.Shown.Count), .. u.Typed];
        frozen = context.Count;
        return new TypoEdit(u.Shown.Count, string.Concat(u.Typed));
    }

    /// `knownLineStart` — перед контекстом точно начало строки (для тестов и пустого поля).
    public void Reset(bool knownLineStart = false)
    {
        leftUnknown = !knownLineStart;
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
        if (extra > 0) leftUnknown = true;
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
