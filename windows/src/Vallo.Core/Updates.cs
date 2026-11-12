namespace Vallo.Core;

/// Обновление, которое ждёт человека. Как на Маке: значок с точкой и строка в меню.
public abstract record PendingUpdate(string Version)
{
    /// Скачано — поставится при выходе или по кнопке.
    public sealed record Ready(string Version) : PendingUpdate(Version);
    /// Вышло, но автообновление выключено — скачаем по кнопке.
    public sealed record Available(string Version) : PendingUpdate(Version);
}

/// Что свитчер делает с обновлением прямо сейчас (или чем кончилась проверка по кнопке) — чтобы человек видел отклик:
/// строка в «О программе», серая строка в меню значка. Ждущее обновление (готово/вышло) — в `PendingUpdate`.
public enum UpdateStage { Checking, Downloading, UpToDate, Failed }

public sealed record UpdateActivity(UpdateStage Stage, string Version = "", int Percent = 0)
{
    /// Идёт работа — кнопки проверки серые.
    public bool Busy => Stage is UpdateStage.Checking or UpdateStage.Downloading;
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

    /// Серая строка меню вместо «Проверить обновления…», пока идёт работа. Нет работы — null.
    public static string? BusyMenuTitle(UpdateActivity? a) => a?.Stage switch
    {
        UpdateStage.Checking => "Проверяю обновления…",
        UpdateStage.Downloading => $"Скачиваю {a.Version} — {a.Percent}%",
        _ => null,
    };

    /// Состояние строки обновления на странице «О программе»: работа важнее ждущего, ждущее — важнее итога прошлой проверки.
    public static (string State, string Version, double Progress)? PageState(UpdateActivity? a, PendingUpdate? pending) => (a, pending) switch
    {
        ({ Stage: UpdateStage.Checking }, _) => ("checking", "", 0),
        ({ Stage: UpdateStage.Downloading } d, _) => ("downloading", d.Version, d.Percent / 100.0),
        (_, PendingUpdate.Ready r) => ("ready", r.Version, 1),
        (_, PendingUpdate.Available v) => ("available", v.Version, 0),
        ({ Stage: UpdateStage.UpToDate }, _) => ("upToDate", "", 0),
        ({ Stage: UpdateStage.Failed }, _) => ("failed", "", 0),
        _ => null,
    };

    public static string DownloadingTitle(string version) => $"Скачиваю {version}…";
    public const string DownloadingBody = "Можно пользоваться свитчером — скажу, когда будет готово";

    public static string ReadyTitle(string version) => $"Обновление {version} готово";
    public const string ReadyBody = "Нажмите, чтобы перезапустить свитчер — это пара секунд";
    public const string UpToDateTitle = "Обновлений нет";
    public static string UpToDateBody(string version) => $"У вас последняя версия — {version}";
    public const string FailedTitle = "Не получилось проверить обновления";
    public const string FailedBody = "Не получилось связаться с GitHub. Попробуйте позже";
}

/// Где свитчер на винде берёт список версий. Файл кладёт CI при выпуске (windows-release.yml) на служебную
/// ветку `win-updates`: это список Велопака (releases.win.json) плюс `baseUrl` — папка выпуска, откуда качать сборки.
/// Обычный файл не упирается в лимит API GitHub (60 запросов в час на адрес без входа).
public static class UpdateFeed
{
    /// По порядку: не открылся первый — пробуем следующий.
    public static readonly IReadOnlyList<string> FeedUrls =
    [
        "https://raw.githubusercontent.com/madvito-collab/vallo_switcher/win-updates/releases.win.json",
    ];

    /// Папка, откуда качать сборки: https и слеш на конце; нет или странная — null.
    public static string? BaseUrl(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("baseUrl", out var b)
                || b.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            var url = b.GetString()!;
            return url.StartsWith("https://", StringComparison.Ordinal) && url.EndsWith('/')
                && Uri.TryCreate(url, UriKind.Absolute, out _) ? url : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// Адрес сборки в папке выпуска. Имя — только имя файла, без путей и адресов.
    public static string? AssetUrl(string baseUrl, string fileName) =>
        fileName.Length == 0 || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(':') || fileName.StartsWith('.')
            ? null
            : baseUrl + Uri.EscapeDataString(fileName);
}
