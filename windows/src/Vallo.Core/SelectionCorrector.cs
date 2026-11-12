namespace Vallo.Core;

/// Исправить выделенный текст (двойной правый Shift), как на Маке: опечатки → «ё» → типографика.
/// Запятые на Windows пока не расставляем — встанут между «ё» и типографикой, когда перенесём модель.
public static class SelectionCorrector
{
    /// `typos` null — словари не открылись: только типографика. `yo` null — «ё» выключена в настройках.
    public static string Fix(string text, TypoCorrector? typos, Yoficator? yo, Language hint)
    {
        var corrected = typos?.CorrectPhrase(text) ?? text;
        var withYo = yo?.FixPhrase(corrected) ?? corrected;
        return Typographer.Format(withYo, final: true, hint: hint);
    }
}
