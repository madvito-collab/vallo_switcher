namespace Vallo.Core;

/// Обновление, которое ждёт человека. Как на Маке: значок с точкой и строка в меню.
public abstract record PendingUpdate(string Version)
{
    /// Скачано — поставится при выходе или по кнопке.
    public sealed record Ready(string Version) : PendingUpdate(Version);
    /// Вышло, но автообновление выключено — скачаем по кнопке.
    public sealed record Available(string Version) : PendingUpdate(Version);
}

/// Когда проверять и что говорить. Тексты — те же, что на Маке.
public static class Updates
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// Пора сходить за обновлениями: ни разу не ходили, прошли сутки или часы перевели назад.
    public static bool IsCheckDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is not { } l || now - l >= Interval || l > now;

    public static string MenuTitle(PendingUpdate p) => p switch
    {
        PendingUpdate.Ready r => $"Перезапустить и обновить до {r.Version}",
        _ => $"Обновить до {p.Version}…",
    };

    public static string ReadyTitle(string version) => $"Обновление {version} готово";
    public const string ReadyBody = "Нажмите, чтобы перезапустить свитчер — это пара секунд";
    public const string UpToDateTitle = "Обновлений нет";
    public static string UpToDateBody(string version) => $"У вас последняя версия — {version}";
    public const string FailedTitle = "Не получилось проверить обновления";
    public const string FailedBody = "Не получилось связаться с GitHub. Попробуйте позже";
}
