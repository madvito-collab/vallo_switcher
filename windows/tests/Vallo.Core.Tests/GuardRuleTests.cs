using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class GuardRuleTests
{
    [Fact]
    public void OrdinaryAppAllowsEverything()
    {
        Assert.True(GuardRule.AutoSwitchAllowed(excluded: false, silent: false, fullscreen: false, password: false));
        Assert.True(GuardRule.ManualAllowed(silent: false, fullscreen: false, password: false));
    }

    [Fact]
    public void ExcludedAppAllowsOnlyManual()
    {
        // как на Маке: набор помним, двойной Shift переводит, сами не переключаем
        Assert.False(GuardRule.AutoSwitchAllowed(excluded: true, silent: false, fullscreen: false, password: false));
        Assert.True(GuardRule.ManualAllowed(silent: false, fullscreen: false, password: false));
    }

    [Fact]
    public void SilentAppIsSilentLikeFullscreen()
    {
        // Майнкрафт в окне: двойной Shift — это игровые клавиши, не просьба переводить
        Assert.False(GuardRule.AutoSwitchAllowed(excluded: false, silent: true, fullscreen: false, password: false));
        Assert.False(GuardRule.ManualAllowed(silent: true, fullscreen: false, password: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FullscreenOrPasswordIsSilent(bool fullscreen, bool password)
    {
        Assert.False(GuardRule.AutoSwitchAllowed(false, false, fullscreen, password));
        Assert.False(GuardRule.ManualAllowed(false, fullscreen, password));
    }
}

public class PasswordRecheckTests
{
    const int Edit = 50004, Document = 50030, Pane = 50033;

    [Fact]
    public void PasswordAnswerIsFinal() =>
        Assert.False(PasswordRecheck.AskAgain(isPassword: true, focusWindowClass: "Chrome_RenderWidgetHostHWND", controlType: Edit));

    [Fact]
    public void ChromiumAsksAgain() =>
        // Chromium строит дерево доступности только после первого вопроса — первый ответ «не пароль» ненадёжен
        Assert.True(PasswordRecheck.AskAgain(isPassword: false, focusWindowClass: "Chrome_RenderWidgetHostHWND", controlType: Edit));

    [Theory]
    [InlineData(Document)]
    [InlineData(Pane)]
    public void DocumentOrPaneAsksAgain(int controlType) =>
        Assert.True(PasswordRecheck.AskAgain(isPassword: false, focusWindowClass: "MozillaWindowClass", controlType: controlType));

    [Fact]
    public void OrdinaryEditIsFinal() =>
        Assert.False(PasswordRecheck.AskAgain(isPassword: false, focusWindowClass: "Edit", controlType: Edit));
}
