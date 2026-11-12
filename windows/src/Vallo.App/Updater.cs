using System.Net.Http.Json;
using System.Text.Json;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;
using Vallo.Core;

namespace Vallo.App;

/// Автообновление, как на Маке: раз в день смотрим список версий (файл на ветке win-updates, не открылся — API GitHub); включено «Обновлять автоматически» —
/// скачиваем сами, и новая версия ставится при выходе (или сразу по кнопке); выключено — только говорим, что вышла.
/// Работает, только если свитчер поставлен установщиком: в распакованном архиве обновлять нечего.
/// Сетевые вызовы — в пуле потоков; всё, что трогает значок и окно, — через `post` на поток трея.
sealed class Updater : IDisposable
{
    const string Repo = "https://github.com/madvito-collab/vallo_switcher";

    readonly SettingsStore store;
    readonly Action<Action> post;
    readonly UpdateManager? manager;
    readonly System.Threading.Timer? timer;
    UpdateInfo? found;
    int busy;

    /// Что ждёт человека. Пишется и читается на потоке трея.
    public PendingUpdate? Pending { get; private set; }
    /// Что делаем прямо сейчас (проверяем, качаем с процентом) или чем кончилась проверка по кнопке. На потоке трея.
    public UpdateActivity? Activity { get; private set; }
    /// Ждущее обновление или работа поменялись. На потоке трея.
    public Action? Changed { get; set; }
    /// Сказать человеку (заголовок, текст, по клику — поставить обновление). На потоке трея.
    public Action<string, string, bool>? Notify { get; set; }
    /// Сейчас перезапустимся с новой версией — убрать значок, чтобы в трее не осталось «призрака». На потоке трея.
    public Action? Restarting { get; set; }

    public bool Supported => manager is not null;

    public Updater(SettingsStore store, Action<Action> post)
    {
        this.store = store;
        this.post = post;
        try
        {
            var m = new UpdateManager(new FallbackSource([new FeedFileSource(), new WindowsReleases(Repo)]));
            if (m.IsInstalled) manager = m;
        }
        catch (Exception) { /* не установлен (архив, отладка) — без обновлений */ }
        if (manager is null) return;
        // первая проверка — через минуту после старта (не мешаем загрузке словарей), дальше — раз в час смотрим, не пора ли
        timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
    }

    void Tick()
    {
        if (Updates.IsCheckDue(store.Current.LastUpdateCheck, DateTimeOffset.Now)) _ = Check(userAsked: false);
    }

    /// «Проверить обновления»: то же, но скажем результат и скачаем, даже если автообновление выключено.
    public void CheckNow() => _ = Check(userAsked: true);

    async Task Check(bool userAsked)
    {
        if (manager is null || Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            Show(new UpdateActivity(UpdateStage.Checking));
            UpdateInfo? info;
            try { info = await manager.CheckForUpdatesAsync().ConfigureAwait(false); }
            catch (Exception e)
            {
                DebugLog.Shared.Write(() => $"обновления: проверить не вышло — {e.GetType().Name}");
                // фоновая проверка не удалась — молчим, как раньше; по кнопке — говорим и показываем в «О программе»
                Show(userAsked ? new UpdateActivity(UpdateStage.Failed) : null);
                if (userAsked) post(() => Notify?.Invoke(Updates.FailedTitle, Updates.FailedBody, false));
                return;
            }
            store.Update(s => s with { LastUpdateCheck = DateTimeOffset.Now });
            DebugLog.Shared.Write(() => info is null ? "обновления: новых нет" : $"обновления: есть {info.TargetFullRelease.Version}");
            if (info is null)
            {
                Show(userAsked ? new UpdateActivity(UpdateStage.UpToDate) : null);
                if (userAsked) post(() => Notify?.Invoke(Updates.UpToDateTitle, Updates.UpToDateBody(Short(manager.CurrentVersion?.ToString() ?? "")), false));
                return;
            }
            var version = Short(info.TargetFullRelease.Version.ToString());
            // уже скачано и ждёт — не качаем заново; по кнопке — напоминаем, что можно перезапустить
            if (Pending is PendingUpdate.Ready ready && ready.Version == version)
            {
                Show(null);
                if (userAsked) post(() => Notify?.Invoke(Updates.ReadyTitle(version), Updates.ReadyBody, true));
                return;
            }
            found = info;
            if (!store.Current.AutoUpdate && !userAsked)
            {
                Show(null);
                post(() => Set(new PendingUpdate.Available(version)));
                return;
            }
            // по кнопке — сразу говорим, что качаем: 57 МБ идут минуту-другую, без отклика кажется, что ничего не происходит
            await Download(info, version, announceStart: userAsked).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    /// Скачать с процентом в меню и в «О программе». Готово — уведомление «Обновление готово» (если не перезапускаемся сразу).
    async Task<bool> Download(UpdateInfo info, string version, bool announceStart, bool announceReady = true)
    {
        Show(new UpdateActivity(UpdateStage.Downloading, version));
        if (announceStart) post(() => Notify?.Invoke(Updates.DownloadingTitle(version), Updates.DownloadingBody, false));
        var shown = 0;
        void Progress(int percent)
        {
            // Велопак зовёт часто — показываем только новые проценты
            var p = Math.Clamp(percent, 0, 100);
            if (Interlocked.Exchange(ref shown, p) != p) Show(new UpdateActivity(UpdateStage.Downloading, version, p));
        }
        try { await manager!.DownloadUpdatesAsync(info, Progress).ConfigureAwait(false); }
        catch (Exception e)
        {
            DebugLog.Shared.Write(() => $"обновления: скачать {version} не вышло — {e.GetType().Name}");
            Show(new UpdateActivity(UpdateStage.Failed));
            post(() => Notify?.Invoke(Updates.FailedTitle, Updates.FailedBody, false));
            return false;
        }
        DebugLog.Shared.Write(() => $"обновления: {version} скачана");
        post(() =>
        {
            Activity = null;
            Set(new PendingUpdate.Ready(version));
            if (announceReady) Notify?.Invoke(Updates.ReadyTitle(version), Updates.ReadyBody, true);
        });
        return true;
    }

    /// Строка в меню, кнопка в «О программе» или клик по уведомлению: скачано — перезапуск с новой версией; нет — скачать и перезапустить.
    public void InstallNow()
    {
        if (manager is null || found is null || Pending is null) return;
        if (Pending is PendingUpdate.Ready) { Apply(); return; }
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        var info = found;
        var version = Pending.Version;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await Download(info, version, announceStart: true, announceReady: false).ConfigureAwait(false)) post(Apply);
            }
            finally { Volatile.Write(ref busy, 0); }
        });
    }

    /// Новое состояние работы — на поток трея, и обновить меню/окно.
    void Show(UpdateActivity? a) => post(() =>
    {
        Activity = a;
        Changed?.Invoke();
    });

    void Apply()
    {
        Restarting?.Invoke();
        try { manager!.ApplyUpdatesAndRestart(found!.TargetFullRelease); }
        catch (Exception) { Notify?.Invoke(Updates.FailedTitle, Updates.FailedBody, false); }
    }

    /// Выход из свитчера (меню, выключение компьютера): скачанное ставится, когда процесс закроется.
    public void BeforeExit()
    {
        if (manager is null || found is null || Pending is not PendingUpdate.Ready) return;
        try { manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: true, restart: false); }
        catch (Exception) { /* не вышло — VelopackApp поставит скачанное при следующем запуске */ }
    }

    void Set(PendingUpdate p)
    {
        Pending = p;
        Changed?.Invoke();
    }

    /// «0.2.0» → «0.2», как пишем версии в выпусках.
    public static string Short(string v) => v.EndsWith(".0", StringComparison.Ordinal) && v.Count(c => c == '.') == 2 ? v[..^2] : v;

    public void Dispose() => timer?.Dispose();

    /// Список версий обычным файлом (UpdateFeed): без лимита API. Сборки — из папки выпуска, адрес которой в самом файле.
    sealed class FeedFileSource : IUpdateSource
    {
        readonly HttpClientFileDownloader downloader = new();
        string? baseUrl;

        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            Exception? last = null;
            foreach (var url in UpdateFeed.FeedUrls)
            {
                try
                {
                    var json = await downloader.DownloadString(url, new Dictionary<string, string>(), 30).ConfigureAwait(false);
                    var b = UpdateFeed.BaseUrl(json) ?? throw new InvalidDataException("в списке версий нет baseUrl");
                    var feed = VelopackAssetFeed.FromJson(json);
                    DebugLog.Shared.Write(() => $"обновления: список версий файлом ({url})");
                    baseUrl = b;
                    return feed;
                }
                catch (Exception e)
                {
                    last = e;
                    DebugLog.Shared.Write(() => $"обновления: файл списка версий не открылся — {e.GetType().Name}");
                }
            }
            throw last ?? new InvalidOperationException("нет адресов списка версий");
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
        {
            var url = baseUrl is null ? null : UpdateFeed.AssetUrl(baseUrl, releaseEntry.FileName);
            if (url is null) throw new InvalidOperationException("не знаем, откуда качать " + releaseEntry.FileName);
            return downloader.DownloadFile(url, localFile, progress, new Dictionary<string, string>(), 30, cancelToken);
        }
    }

    /// Пробуем источники по порядку; качаем из того, что дал список версий.
    sealed class FallbackSource(IUpdateSource[] sources) : IUpdateSource
    {
        IUpdateSource? used;

        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            Exception? last = null;
            foreach (var s in sources)
            {
                try
                {
                    var feed = await s.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease).ConfigureAwait(false);
                    used = s;
                    return feed;
                }
                catch (Exception e) { last = e; }
            }
            throw last!;
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default) =>
            (used ?? throw new InvalidOperationException("список версий ещё не получен"))
                .DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }

    /// Запасной путь (как было до 0.3) — выпуски на GitHub через API: Велопак смотрит только 10 последних, а маковских выходит больше —
    /// берём до 100 и оставляем только те, где есть список виндовой версии (releases.win.json).
    sealed class WindowsReleases(string repo) : GithubSource(repo, null, false)
    {
        static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ValloSwitcher");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var all = await http.GetFromJsonAsync<GithubRelease[]>(
                "https://api.github.com/repos/madvito-collab/vallo_switcher/releases?per_page=100", Json).ConfigureAwait(false) ?? [];
            return all
                .Where(r => includePrereleases || !r.Prerelease)
                .Where(r => r.Assets?.Any(a => a.Name?.StartsWith("releases.", StringComparison.Ordinal) == true) == true)
                .OrderByDescending(r => r.PublishedAt)
                .ToArray();
        }
    }
}
