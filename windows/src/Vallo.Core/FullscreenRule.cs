namespace Vallo.Core;

/// Прямоугольник в пикселях экрана: правый и нижний края — не включительно (как RECT в Windows).
public readonly record struct Rect(int Left, int Top, int Right, int Bottom);

/// Игра или видео на весь экран — там свитчер молчит.
public static class FullscreenRule
{
    /// `monitor` — весь монитор (rcMonitor), не рабочая область.
    /// Рабочий стол и панель задач тоже «во весь экран», но это не игра; развёрнутое окно с заголовком
    /// накрывает монитор, когда панель задач прячется сама, — тоже не игра.
    public static bool IsFullscreen(Rect window, Rect monitor, bool isDesktopOrShell, bool isMaximizedWithCaption)
    {
        if (isDesktopOrShell || isMaximizedWithCaption) return false;
        if (monitor.Right <= monitor.Left || monitor.Bottom <= monitor.Top) return false;
        return window.Left <= monitor.Left && window.Top <= monitor.Top
            && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }
}
