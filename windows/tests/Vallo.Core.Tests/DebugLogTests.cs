using Vallo.Core;
using Xunit;
using static Vallo.Core.EngineAction;

namespace Vallo.Core.Tests;

public class DebugLogTests
{
    static string TempLog() => Path.Combine(Path.GetTempPath(), "vallo-log-" + Guid.NewGuid().ToString("N"), "debug.log");

    [Fact]
    public void WritesOnlyWhenEnabled()
    {
        var path = TempLog();
        using var log = new DebugLog(path);
        log.Write("до включения");
        log.Enabled = true;
        log.Write("key → consume");
        log.Write(() => "собрано лениво");
        log.Enabled = false;
        log.Write("после выключения");
        log.Flush();
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("до включения", text);
        Assert.Contains("журнал включён", text);
        Assert.Contains("key → consume", text);
        Assert.Contains("собрано лениво", text);
        Assert.Contains("журнал выключен", text);
        Assert.DoesNotContain("после выключения", text);
    }

    [Fact]
    public void BigFileMovesToOld()
    {
        var path = TempLog();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', (int)DebugLog.MaxBytes));
        using var log = new DebugLog(path) { Enabled = true };
        log.Write("новая строка");
        log.Flush();
        Assert.True(new FileInfo(log.OldPath).Length >= DebugLog.MaxBytes);
        Assert.True(new FileInfo(path).Length < 1000);
    }

    [Fact]
    public void SummaryHasNoText()
    {
        var s = LogText.Summary([new Delete(6), new Insert("привет "), new SwitchLayout(Language.Ru), new Learn("ghbdtn"), new PressKey(0x0D)]);
        Assert.Equal("delete(6) insert(7 chars) switch(ru) learn key(0x0D)", s);
        Assert.DoesNotContain("привет", s);
        Assert.DoesNotContain("ghbdtn", s);
    }
}
