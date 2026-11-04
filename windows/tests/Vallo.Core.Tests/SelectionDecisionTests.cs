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
