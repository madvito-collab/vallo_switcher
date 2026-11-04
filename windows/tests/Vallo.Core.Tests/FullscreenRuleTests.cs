using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class FullscreenRuleTests
{
    static readonly Rect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void WindowEqualToMonitorIsFullscreen() =>
        Assert.True(FullscreenRule.IsFullscreen(Monitor, Monitor, isDesktopOrShell: false, isMaximizedWithCaption: false));

    [Fact]
    public void WorkAreaWithoutTaskbarIsNot() =>
        Assert.False(FullscreenRule.IsFullscreen(new Rect(0, 0, 1920, 1040), Monitor, false, false));

    [Fact]
    public void MaximizedWindowWithCaptionIsNot() =>
        // панель задач скрывается сама — развёрнутое окно (с рамкой за краями) накрывает монитор
        Assert.False(FullscreenRule.IsFullscreen(new Rect(-8, -8, 1928, 1088), Monitor, false, isMaximizedWithCaption: true));

    [Fact]
    public void BorderlessWindowedGameIsFullscreen() =>
        Assert.True(FullscreenRule.IsFullscreen(new Rect(0, 0, 1920, 1080), Monitor, false, false));

    [Fact]
    public void WholeSecondMonitorIsFullscreen()
    {
        var second = new Rect(1920, 0, 3840, 1080);
        Assert.True(FullscreenRule.IsFullscreen(second, second, false, false));
    }

    [Fact]
    public void DesktopIsNot() =>
        Assert.False(FullscreenRule.IsFullscreen(Monitor, Monitor, isDesktopOrShell: true, false));
}
