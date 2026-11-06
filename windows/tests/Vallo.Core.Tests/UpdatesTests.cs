using System.Text.Json;
using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

/// Автообновление на Windows — как на Маке: раз в день, точка на значке, строка в меню.
public class UpdatesTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CheckIsDueOncePerDay()
    {
        Assert.True(Updates.IsCheckDue(null, Now));
        Assert.False(Updates.IsCheckDue(Now.AddHours(-23), Now));
        Assert.True(Updates.IsCheckDue(Now.AddHours(-24), Now));
        // часы перевели назад — проверка из «будущего» не должна заблокировать проверки навсегда
        Assert.True(Updates.IsCheckDue(Now.AddDays(3), Now));
    }

    [Fact]
    public void MenuTitlesLikeMac()
    {
        Assert.Equal("Перезапустить и обновить до 0.2", Updates.MenuTitle(new PendingUpdate.Ready("0.2")));
        Assert.Equal("Обновить до 0.2…", Updates.MenuTitle(new PendingUpdate.Available("0.2")));
    }

    [Fact]
    public void AutoUpdateOnByDefaultAndSurvivesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vallo-{Guid.NewGuid()}.json");
        try
        {
            Assert.True(new AppSettings().AutoUpdate);
            new AppSettings { AutoUpdate = false, LastUpdateCheck = Now }.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.False(loaded.AutoUpdate);
            Assert.Equal(Now, loaded.LastUpdateCheck);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OldSettingsFileGetsAutoUpdateOn()
    {
        // файл от версии без автообновления — поля нет, значит включено
        var path = Path.Combine(Path.GetTempPath(), $"vallo-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, """{ "autoSwitchEnabled": true }""");
            Assert.True(AppSettings.Load(path).AutoUpdate);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SettingsPageShowsUpdatesOnlyWhenInstalled()
    {
        var s = new AppSettings { LastUpdateCheck = Now };
        var portable = SettingsPage.State(s, false, "0.1.0", null, e => e);
        Assert.False((bool)portable["about"]!["updates"]!);
        var installed = SettingsPage.State(s, false, "0.1.0", null, e => e, updates: true);
        Assert.True((bool)installed["about"]!["updates"]!);
        Assert.True((bool)installed["about"]!["autoUpdate"]!);
        Assert.Equal("2026-11-06T12:00:00Z", (string?)installed["about"]!["lastCheck"]);
    }

    [Fact]
    public void SettingsPageTogglesAutoUpdate()
    {
        var s = SettingsPage.Apply(new AppSettings(), "autoUpdate", JsonDocument.Parse("false").RootElement);
        Assert.False(s.AutoUpdate);
    }
}
