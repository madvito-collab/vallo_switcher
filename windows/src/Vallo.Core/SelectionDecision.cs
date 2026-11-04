namespace Vallo.Core;

/// Что дал Ctrl+C: буфер обмена до и после.
public static class SelectionDecision
{
    public abstract record Result
    {
        public sealed record NoSelection : Result;
        public sealed record Selected(string Text) : Result;
    }

    /// Пусто или то же, что было, — Ctrl+C ничего не скопировал, выделения нет.
    public static Result Decide(string? before, string? after) =>
        string.IsNullOrEmpty(after) || after == before ? new Result.NoSelection() : new Result.Selected(after);
}

/// Можно ли читать выделение через Ctrl+C. В терминалах и редакторах кода (исключения) Ctrl+C — не «копировать»:
/// прерывание процесса или копирование всей строки. Консоль — даже если её убрали из исключений.
/// На третьей раскладке переводить не во что.
public static class SelectionPolicy
{
    static readonly HashSet<string> ConsoleClasses = new(StringComparer.Ordinal)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS",
    };

    public static bool MayCopy(bool excludedApp, string? windowClass, bool foreignLayout) =>
        !excludedApp && !foreignLayout && !(windowClass is not null && ConsoleClasses.Contains(windowClass));
}
