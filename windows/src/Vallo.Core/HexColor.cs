namespace Vallo.Core;

/// Цвет, набранный в русской раскладке: клавиши a–f дают ф, и, с, в, у, а, а Shift+3 — «№» вместо «#».
/// «№аааааа» → «#ffffff», «#1ф2и3с» → «#1a2b3c»; в поле цвета Фигмы — и без решётки: «ааа» → «fff».
/// Только 3, 6 или 8 знаков (#rgb, #rrggbb, #rrggbbaa). Порт HexColor.swift.
public static class HexColor
{
    static readonly Dictionary<char, char> Keys = new()
    {
        ['ф'] = 'a', ['и'] = 'b', ['с'] = 'c', ['в'] = 'd', ['у'] = 'e', ['а'] = 'f',
        ['Ф'] = 'A', ['И'] = 'B', ['С'] = 'C', ['В'] = 'D', ['У'] = 'E', ['А'] = 'F',
    };

    /// Исправленный цвет или null — это не цвет (или исправлять нечего).
    /// `requireHash` — нужна решётка (везде, кроме поля цвета Фигмы).
    public static string? Fix(string token, bool requireHash)
    {
        var body = token;
        char? prefix = null;
        if (body.Length > 0 && (body[0] == '#' || body[0] == '№'))
        {
            prefix = body[0];
            body = body[1..];
        }
        else if (requireHash) return null;
        if (body.Length is not (3 or 6 or 8)) return null;
        var sb = new System.Text.StringBuilder();
        bool russian = false, letter = false;
        foreach (var ch in body)
        {
            if (Keys.TryGetValue(ch, out var latin)) { sb.Append(latin); russian = true; letter = true; }
            else if (char.IsAsciiHexDigit(ch)) { sb.Append(ch); if (char.IsAsciiLetter(ch)) letter = true; }
            else return null;
        }
        // «№ 123456» без букв — номер, а не цвет; «#abc» латиницей — уже правильно
        if (!letter || !(russian || prefix == '№')) return null;
        return prefix is null ? sb.ToString() : "#" + sb;
    }
}
