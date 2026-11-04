namespace Vallo.Core;

/// Приложения, где автопереключение выключено (терминалы, редакторы кода, Майнкрафт).
public static class ExclusionMatcher
{
    /// Сравниваем только имя файла, без учёта регистра: «C:\…\JAVAW.EXE» = «javaw.exe».
    public static bool IsExcluded(string exeName, IEnumerable<string> excluded)
    {
        if (string.IsNullOrEmpty(exeName)) return false;
        var name = Path.GetFileName(exeName.Replace('\\', '/'));
        foreach (var e in excluded)
            if (string.Equals(name, e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
