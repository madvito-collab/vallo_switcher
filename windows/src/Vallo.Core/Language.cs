namespace Vallo.Core;

public enum Language { En, Ru }

public static class LanguageExtensions
{
    public static Language Other(this Language l) => l == Language.En ? Language.Ru : Language.En;

    /// Как язык записан в JSON: «en» / «ru».
    public static string Code(this Language l) => l == Language.En ? "en" : "ru";

    public static bool TryParse(string? code, out Language language)
    {
        switch (code)
        {
            case "en": language = Language.En; return true;
            case "ru": language = Language.Ru; return true;
            default: language = default; return false;
        }
    }
}
