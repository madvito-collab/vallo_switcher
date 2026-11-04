namespace Vallo.Core;

/// Где свитчер молчит.
/// - исключения (терминалы, редакторы кода): автопереключения нет, двойной Shift переводит набранное — как на Маке;
/// - «тихие» приложения (игры в окне), игра/видео на весь экран, поле пароля: молчит всё.
public static class GuardRule
{
    public static bool AutoSwitchAllowed(bool excluded, bool silent, bool fullscreen, bool password) =>
        !excluded && ManualAllowed(silent, fullscreen, password);

    public static bool ManualAllowed(bool silent, bool fullscreen, bool password) =>
        !silent && !fullscreen && !password;
}

/// Поле пароля через UI Automation: когда первому ответу «не пароль» верить нельзя.
public static class PasswordRecheck
{
    public const string ChromiumContentClass = "Chrome_RenderWidgetHostHWND";
    public const int DocumentControlType = 50030, PaneControlType = 50033;

    /// Chromium/Electron строит дерево доступности только после первого вопроса, и на первый вопрос
    /// отвечает документ или панель, а не само поле, — спросить ещё раз.
    public static bool AskAgain(bool isPassword, string? focusWindowClass, int controlType) =>
        !isPassword && (focusWindowClass == ChromiumContentClass
                        || controlType is DocumentControlType or PaneControlType);
}
