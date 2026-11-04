namespace Vallo.Core;

/// Какую картинку «ё» ставить в трей.
public static class TrayIconChoice
{
    // 18 и 22 нарочно нет: таких размеров у шага DPI Windows не бывает.
    static readonly int[] Sizes = [16, 20, 24, 32, 44, 64];

    /// Светлая панель задач — чёрная «ё», тёмная — белая; размер — ближайший ≥ 16 * dpi / 96.
    public static string FileName(bool taskbarIsLight, int dpi)
    {
        if (dpi <= 0) dpi = 96;
        var need = 16.0 * dpi / 96;
        var size = Sizes.FirstOrDefault(s => s >= need, Sizes[^1]);
        return $"yo_{(taskbarIsLight ? "black" : "white")}_{size}.png";
    }
}
