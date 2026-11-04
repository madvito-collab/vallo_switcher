using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class TrayIconChoiceTests
{
    [Theory]
    [InlineData(true, 96, "yo_black_16.png")]
    [InlineData(false, 144, "yo_white_24.png")]
    [InlineData(false, 120, "yo_white_20.png")]
    [InlineData(true, 108, "yo_black_20.png")]  // 18 → нет 18, берём 20
    [InlineData(true, 168, "yo_black_32.png")]  // 28 → 32
    [InlineData(false, 192, "yo_white_32.png")]
    [InlineData(false, 240, "yo_white_44.png")] // 40 → 44
    [InlineData(true, 288, "yo_black_64.png")]  // 48 → 64
    [InlineData(true, 960, "yo_black_64.png")]  // больше всех — самый крупный
    [InlineData(true, 0, "yo_black_16.png")]    // мусорный dpi — как 96
    public void PicksFile(bool light, int dpi, string expected)
    {
        Assert.Equal(expected, TrayIconChoice.FileName(light, dpi));
    }
}
