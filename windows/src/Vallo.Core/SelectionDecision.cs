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
