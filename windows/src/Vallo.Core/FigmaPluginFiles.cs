namespace Vallo.Core;

/// Копия плагина для Фигмы вне папки программы: путь короче и не меняется от версии к версии.
/// Перезаписывается при каждом запуске — после обновления свитчера Фигма подхватит новый плагин.
/// Как `FigmaBridge.installPluginFiles()` на Маке.
public static class FigmaPluginFiles
{
    public static readonly string[] Names = ["manifest.json", "code.js", "ui.html"];

    /// %LOCALAPPDATA%\Vallo Switcher\Figma Plugin — отсюда плагин подключают в Фигме («Import plugin from manifest»).
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vallo Switcher", "Figma Plugin");

    /// false — не скопировалось (в журнал отладки — почему).
    public static bool Install(string source, string target)
    {
        try
        {
            Directory.CreateDirectory(target);
            foreach (var name in Names) File.Copy(Path.Combine(source, name), Path.Combine(target, name), overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DebugLog.Shared.Write(() => $"figma: плагин не скопировался: {e.GetType().Name}");
            return false;
        }
    }
}
