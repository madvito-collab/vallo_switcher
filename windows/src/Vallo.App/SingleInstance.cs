using System.Runtime.InteropServices;

namespace Vallo.App;

/// Один экземпляр на сеанс пользователя: именованный мьютекс. Запустили ещё раз (ярлык в «Пуске», exe) —
/// второй экземпляр будит первого именованным событием и выходит, первый открывает настройки (как на Маке).
static class SingleInstance
{
    const string Name = @"Local\ValloSwitcher";
    const string ShowName = @"Local\ValloSwitcher.ShowSettings";

    /// null — уже запущен другой экземпляр.
    public static Mutex? TryAcquire()
    {
        var m = new Mutex(initiallyOwned: true, Name, out var createdNew);
        if (createdNew) return m;
        m.Dispose();
        return null;
    }

    /// Первый экземпляр: событие «покажи настройки». Создаём сразу после мьютекса, чтобы второй его застал.
    public static EventWaitHandle CreateShowSignal() => new(false, EventResetMode.AutoReset, ShowName);

    /// Второй экземпляр: попросить первого показать настройки. Нет события — первый ещё не успел, ничего страшного.
    public static void RequestShow()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ShowName, out var e)) return;
            // мы — программа, которую только что запустили, нам можно на передний план; первому — нет, пока не разрешим
            AllowSetForegroundWindow(ASFW_ANY);
            using (e) e.Set();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    /// Ждать сигнала в фоновом потоке; `show` — на каждый повторный запуск.
    public static void Listen(EventWaitHandle signal, Action show)
    {
        var t = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne()) show();
            }
            catch (ObjectDisposedException) { /* выход */ }
        }) { IsBackground = true, Name = "Vallo show settings" };
        t.Start();
    }

    const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
