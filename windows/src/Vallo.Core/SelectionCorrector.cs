namespace Vallo.Core;

/// Исправить выделенный текст (двойной правый Shift), как на Маке: опечатки → «ё» → запятые → типографика.
public static class SelectionCorrector
{
    /// С запятыми: модель думает в фоне; не скачана, не загрузилась или дольше таймаута — без запятых.
    /// `capitalize` — «Заглавная буква и точка».
    public static async Task<string> FixAsync(string text, TypoCorrector? typos, Yoficator? yo, CommaService? commas,
                                              bool capitalize, Language hint)
    {
        var corrected = typos?.CorrectPhrase(text) ?? text;
        var withYo = yo?.FixPhrase(corrected) ?? corrected;
        // фильтру запятых нужен исправлятор опечаток: без словарей модель не зовём
        var withCommas = typos is not null && commas is not null
            ? await commas.FixAsync(withYo, typos, capitalize).ConfigureAwait(false)
            : null;
        return Typographer.Format(withCommas ?? withYo, final: true, hint: hint);
    }

    /// `typos` null — словари не открылись: только типографика. `yo` null — «ё» выключена в настройках.
    public static string Fix(string text, TypoCorrector? typos, Yoficator? yo, Language hint)
    {
        var corrected = typos?.CorrectPhrase(text) ?? text;
        var withYo = yo?.FixPhrase(corrected) ?? corrected;
        return Typographer.Format(withYo, final: true, hint: hint);
    }
}
