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
        if (!store.Current.FirstRunDone && Autostart.Enable())
            store.Update(s => s with { FirstRunDone = true });

        using var tray = new Tray(store, Path.Combine(AppContext.BaseDirectory, "Icons"));
        // словари грузятся секунду-другую — значок в трее показываем, не дожидаясь
        SwitcherService? switcher = null;
        var starting = Task.Run(() =>
        {
            try { switcher = SwitcherService.Start(store); }
            catch { } // без свитчера трей всё равно работает
        });
        var code = tray.Run();
        starting.Wait();
        switcher?.Dispose();
        return code;
    }
}
