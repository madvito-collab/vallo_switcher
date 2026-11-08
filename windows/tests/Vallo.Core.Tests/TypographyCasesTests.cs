using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

/// Общие примеры типографики (`Data/tests/typography-cases.tsv`) — те же гоняет Мак, он эталон.
public class TypographyCasesTests
{
    public static IEnumerable<object[]> Cases() =>
        File.ReadAllLines(TestSupport.RepoData("Data/tests/typography-cases.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t').Select(f => f.Replace("⍽", " ")).ToArray())
            .Where(p => p.Length == 4)
            .Select(p => new object[] { p[0], p[1], p[2], p[3] });

    /// Набор по символу: что окажется на экране (типографика + смайлики «Миллениал»).
    static string Live(string text)
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        var options = new LiveOptions(Typography: true, Emoji: EmojiBrackets.Millennial);
        var screen = new List<string>();
        foreach (var ch in TextElements.Split(text))
        {
            if (live.Typed(ch, Language.Ru, options) is { } e)
            {
                screen.RemoveRange(screen.Count - e.Delete, e.Delete);
                screen.AddRange(TextElements.Split(e.Insert));
            }
            else screen.Add(ch);
        }
        return string.Concat(screen);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string mode, string input, string output, string note)
    {
        var got = mode switch
        {
            "final" => Typographer.Format(input),
            "draft" => Typographer.Format(input, final: false),
            "live" => Live(input),
            _ => throw new InvalidDataException(mode),
        };
        Assert.True(got == output, $"{mode} «{input}» → «{got.Replace(' ', '⍽')}», ждали «{output.Replace(' ', '⍽')}» — {note}");
    }

    [Fact]
    public void FileIsThere() => Assert.True(Cases().Count() >= 80);
}
