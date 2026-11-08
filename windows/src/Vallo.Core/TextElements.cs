using System.Globalization;

namespace Vallo.Core;

/// Символы так, как их видит человек: смайлик 🙂 — один символ (в C# это две половинки UTF-16).
/// Backspace стирает именно такой символ, поэтому правки считаем в них.
public static class TextElements
{
    public static List<string> Split(string s)
    {
        var list = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) list.Add((string)e.Current);
        return list;
    }

    public static int Count(string s) => new StringInfo(s).LengthInTextElements;

    public static bool IsLetter(string element) => element.Length > 0 && char.IsLetter(element, 0);
}
