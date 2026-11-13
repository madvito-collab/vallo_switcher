using Vallo.Core;

namespace Vallo.App;

static class Program
{
    [STAThread]
    static int Main()
    {
        // установщик и обновления: на служебных запусках (установка, удаление) Velopack делает своё и выходит;
        // скачанное, но не поставленное обновление ставит здесь же
        Velopack.VelopackApp.Build().Run();
        using var single = SingleInstance.TryAcquire();
        if (single is null)
        {
            // уже запущен — значок в трее, скорее всего, не нашли: пусть первый откроет настройки
            SingleInstance.RequestShow();
            return 0;
        }
        using var showSignal = SingleInstance.CreateShowSignal();

        var store = new SettingsStore(AppSettings.DefaultPath);
        DebugLog.Shared.Enabled = store.Current.DebugMode;
        DebugLog.Shared.Write(() => $"запуск {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)}, Windows {Environment.OSVersion.Version}");
        // включён автозапуск — путь к exe переписываем каждый раз: папку могли перенести
        if (AutostartRule.ShouldWrite(store.Current.FirstRunDone, Autostart.IsEnabled)
            && Autostart.Enable() && !store.Current.FirstRunDone)
            store.Update(s => s with { FirstRunDone = true });

        using var tray = new Tray(store, Path.Combine(AppContext.BaseDirectory, "Icons"));
        // словари грузятся секунду-другую — значок в трее показываем, не дожидаясь
        SwitcherService? switcher = null;
        tray.Resumed = () => Volatile.Read(ref switcher)?.ReinstallHooks();
        using var settings = new SettingsWindow(store, () => Volatile.Read(ref switcher)?.SettingsChanged());
        tray.OpenSettings = page => settings.Show(page);
        SingleInstance.Listen(showSignal, () => tray.Post(() =>
        {
            DebugLog.Shared.Write("повторный запуск → настройки");
            settings.Show("general");
        }));
        using var updater = new Updater(store, tray.Post);
        tray.Updater = updater;
        tray.BeforeQuit = updater.BeforeExit;
        updater.Changed = () =>
        {
            tray.ShowBadge(updater.Pending is not null);
            settings.Refresh();
        };
        updater.Notify = tray.Notify;
        updater.Restarting = tray.Hide;
        settings.Updater = updater;
        settings.Switcher = () => Volatile.Read(ref switcher);
        using var models = new ModelStore();
        models.Changed = () => tray.Post(settings.Refresh);
        settings.Models = models;
        // модель запятых: удалили или заменили — выгрузить; не загрузилась (битые файлы) — предложить скачать заново
        using var commas = new CommaService(models.Directory);
        models.ModelReplaced = commas.Unload;
        commas.LoadFailed = models.MarkBroken;
        var starting = Task.Run(() =>
        {
            var problem = StartProblem.Unknown;
            try
            {
                var started = SwitcherService.Start(store, commas, out problem);
                if (started is not null) started.TypoWordsChanged = () => tray.Post(settings.Refresh);
                Volatile.Write(ref switcher, started);
            }
            catch { problem = StartProblem.Unknown; }
            // без свитчера трей всё равно работает — но человек должен знать, что свитчер не запущен
            if (Volatile.Read(ref switcher) is null) tray.ShowProblem(StartProblems.MenuText(problem));
            DebugLog.Shared.Write(() => Volatile.Read(ref switcher) is null ? $"свитчер не запущен: {problem}" : "свитчер запущен");
        });
        var code = tray.Run();
        starting.Wait();
        switcher?.Dispose();
        DebugLog.Shared.Write("выход");
        DebugLog.Shared.Flush();
        return code;
    }
}
