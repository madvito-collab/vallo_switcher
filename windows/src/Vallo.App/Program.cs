using Vallo.Core;

namespace Vallo.App;

static class Program
{
    [STAThread]
    static int Main()
    {
        using var single = SingleInstance.TryAcquire();
        if (single is null) return 0; // уже запущен

        var store = new SettingsStore(AppSettings.DefaultPath);
        // включён автозапуск — путь к exe переписываем каждый раз: папку могли перенести
        if (AutostartRule.ShouldWrite(store.Current.FirstRunDone, Autostart.IsEnabled)
            && Autostart.Enable() && !store.Current.FirstRunDone)
            store.Update(s => s with { FirstRunDone = true });

        using var tray = new Tray(store, Path.Combine(AppContext.BaseDirectory, "Icons"));
        // словари грузятся секунду-другую — значок в трее показываем, не дожидаясь
        SwitcherService? switcher = null;
        tray.Resumed = () => Volatile.Read(ref switcher)?.ReinstallHooks();
        var starting = Task.Run(() =>
        {
            var problem = StartProblem.Unknown;
            try { Volatile.Write(ref switcher, SwitcherService.Start(store, out problem)); }
            catch { problem = StartProblem.Unknown; }
            // без свитчера трей всё равно работает — но человек должен знать, что свитчер не запущен
            if (Volatile.Read(ref switcher) is null) tray.ShowProblem(StartProblems.MenuText(problem));
        });
        var code = tray.Run();
        starting.Wait();
        switcher?.Dispose();
        return code;
    }
}
