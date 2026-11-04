using System.Security;
using Microsoft.Win32;

namespace Vallo.App;

/// Запуск при входе: HKCU\...\Run, значение «Vallo Switcher» = путь к exe.
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Vallo Switcher";

    public static bool Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
            {
                return false;
            }
        }
    }
}
