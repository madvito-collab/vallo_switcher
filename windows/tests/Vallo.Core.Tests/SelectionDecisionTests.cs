using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class SelectionDecisionTests
{
    [Fact]
    public void EmptyAfterIsNoSelection() =>
        Assert.IsType<SelectionDecision.Result.NoSelection>(SelectionDecision.Decide(null, ""));

    [Fact]
    public void UnchangedClipboardIsNoSelection() =>
        Assert.IsType<SelectionDecision.Result.NoSelection>(SelectionDecision.Decide("abc", "abc"));

    [Fact]
    public void NewTextIsSelected()
    {
        var r = Assert.IsType<SelectionDecision.Result.Selected>(SelectionDecision.Decide("abc", "ghbdtn"));
        Assert.Equal("ghbdtn", r.Text);
    }
}

public class SelectionPolicyTests
{
    [Fact]
    public void OrdinaryWindowMayCopy() =>
        Assert.True(SelectionPolicy.MayCopy(excludedApp: false, windowClass: "Chrome_WidgetWin_1", foreignLayout: false));

    [Fact]
    public void ExcludedAppNeverCopies() =>
        Assert.False(SelectionPolicy.MayCopy(excludedApp: true, windowClass: "SunAwtFrame", foreignLayout: false));

    [Theory]
    [InlineData("ConsoleWindowClass")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    public void ConsoleNeverCopiesEvenIfNotExcluded(string cls) =>
        Assert.False(SelectionPolicy.MayCopy(excludedApp: false, windowClass: cls, foreignLayout: false));

    [Fact]
    public void ThirdLayoutNeverCopies() =>
        Assert.False(SelectionPolicy.MayCopy(excludedApp: false, windowClass: "Notepad", foreignLayout: true));
}

public class EngineForeignLayoutTests
{
    [Fact]
    public void ThirdLayoutIsVisible()
    {
        var e = EngineSupport.MakeEngine();
        Assert.False(e.IsForeignLayout);
        e.ExternalLayoutChange(null);
        Assert.True(e.IsForeignLayout);
        e.ExternalLayoutChange(Language.Ru);
        Assert.False(e.IsForeignLayout);
    }

    [Fact]
    public void AutoSwitchOffStillBuffersForManualConvert()
    {
        // исключённое приложение: автопереключения нет, но набранное двойной Shift переводит
        var e = EngineSupport.MakeEngine(auto: false);
        Assert.True(EngineSupport.AllPass(EngineSupport.Type("ghbdtn", e)));
        Assert.True(e.HasTypedText);
        Assert.NotEmpty(e.HandleDoubleShift(null));
    }
}
