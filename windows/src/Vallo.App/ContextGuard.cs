using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// «Охранник»: можно ли сейчас переключать. Перехват только читает готовый ответ —
/// сам ответ считается на своём потоке при смене окна/фокуса (и раз в полсекунды: F11 окно не меняет).
///
/// - приложение в исключениях → молчит автопереключение, двойной Shift работает (как на Маке);
/// - игра/видео на весь экран, поле пароля → молчит всё.
sealed unsafe class ContextGuard : IDisposable
{
    const int RecheckMs = 500;
    const int PasswordBudgetMs = 50;

    readonly SettingsStore store;
    readonly Thread thread;
    readonly AutoResetEvent changed = new(false);
    readonly PasswordProbe probe;
    volatile bool stopping;
    /// Сменились окно или фокус — спросить про пароль заново.
    int focusDirty = 1;

    volatile bool excluded, fullscreen, password;
    volatile string windowClass = "";

    // --- только поток охранника ---
    uint lastPid;
    string lastExe = "";

    public ContextGuard(SettingsStore store)
    {
        this.store = store;
        probe = new PasswordProbe(this);
        thread = new Thread(Loop) { IsBackground = true, Name = "Vallo guard" };
    }

    public void Start()
    {
        probe.Start();
        thread.Start();
        changed.Set(); // первое окно — сразу, не через полсекунды
    }

    /// Автопереключение: не исключение, не полный экран, не пароль.
    public bool AutoSwitchAllowed() => !excluded && !fullscreen && !password;

    /// Двойной Shift: в исключениях можно, в игре и в поле пароля — нет.
    public bool ManualAllowed() => !fullscreen && !password;

    /// Приложение в исключениях: набор помним, но сами не переключаем и выделение через Ctrl+C не читаем.
    public bool InExcludedApp => excluded;

    /// Класс активного окна (консоль узнаём по нему).
    public string WindowClass => windowClass;

    /// Из перехвата: только отметить и разбудить — сам ни о чём не спрашивает.
    public void Invalidate()
    {
        Volatile.Write(ref focusDirty, 1);
        changed.Set();
    }

    public void Dispose()
    {
        stopping = true;
        changed.Set();
        if (thread.IsAlive) thread.Join(1000);
        probe.Dispose();
        changed.Dispose();
    }

    void Loop()
    {
        while (!stopping)
        {
            changed.WaitOne(RecheckMs);
            if (stopping) break;
            try { Refresh(); }
            catch
            {
                // не поняли, где мы, — не мешаем
                excluded = fullscreen = password = false;
                windowClass = "";
            }
        }
    }

    void Refresh()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            excluded = fullscreen = password = false;
            windowClass = "";
            return;
        }
        excluded = ExclusionMatcher.IsExcluded(ExeName(hwnd), store.Current.ExcludedApps);
        var cls = ClassOf(hwnd);
        windowClass = cls;
        fullscreen = IsFullscreen(hwnd, cls);
        if (Interlocked.Exchange(ref focusDirty, 0) == 1)
            probe.Ask(PasswordBudgetMs);
    }

    /// Имя exe активного окна; не узнали (процесс с правами администратора) — пусто, значит «можно».
    string ExeName(nint hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, &pid);
        if (pid == lastPid) return lastExe;
        var exe = "";
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != 0)
        {
            try
            {
                var buf = stackalloc char[1024];
                uint size = 1024;
                if (QueryFullProcessImageName(process, 0, buf, ref size)) exe = Path.GetFileName(new string(buf, 0, (int)size));
            }
            finally
            {
                CloseHandle(process);
            }
        }
        lastPid = pid;
        lastExe = exe;
        return exe;
    }

    static string ClassOf(nint hwnd)
    {
        var buf = stackalloc char[256];
        var len = GetClassName(hwnd, buf, 256);
        return len > 0 ? new string(buf, 0, len) : "";
    }

    static bool IsFullscreen(nint hwnd, string cls)
    {
        if (!GetWindowRect(hwnd, out var w)) return false;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;
        var m = info.rcMonitor; // весь монитор, не рабочая область

        var shell = cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        var maximizedWithCaption = IsZoomed(hwnd) && ((long)GetWindowLongPtr(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;

        return FullscreenRule.IsFullscreen(new Rect(w.left, w.top, w.right, w.bottom),
            new Rect(m.left, m.top, m.right, m.bottom), shell, maximizedWithCaption);
    }

    /// Пишет только PasswordProbe и только под своим замком — ответы не перепутаются.
    void SetPassword(bool value) => password = value;

    /// Поле пароля — через UI Automation на своём потоке (MTA): зависшее приложение держит только его.
    sealed class PasswordProbe(ContextGuard owner) : IDisposable
    {
        static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
        static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
        const int UIA_IsPasswordPropertyId = 30019;
        // номера в таблицах методов: IUIAutomation::GetFocusedElement, IUIAutomationElement::GetCurrentPropertyValue
        const int SlotRelease = 2, SlotGetFocusedElement = 8, SlotGetCurrentPropertyValue = 10;

        readonly AutoResetEvent request = new(false);
        readonly object answerGate = new();
        volatile bool stopping;
        Thread? worker;
        int asked, answered;

        public void Start()
        {
            worker = new Thread(Run) { IsBackground = true, Name = "Vallo uia" };
            worker.Start();
        }

        /// Спросить и записать ответ охраннику. Не ответили за `budgetMs` или ошибка — «можно»;
        /// поздний ответ на тот же фокус всё равно запишется.
        public void Ask(int budgetMs)
        {
            int ticket;
            lock (answerGate) ticket = ++asked;
            request.Set();
            var deadline = Environment.TickCount64 + budgetMs;
            lock (answerGate)
            {
                while (answered < ticket)
                {
                    var left = deadline - Environment.TickCount64;
                    if (left <= 0)
                    {
                        owner.SetPassword(false);
                        return;
                    }
                    Monitor.Wait(answerGate, (int)left);
                }
            }
        }

        public void Dispose()
        {
            stopping = true;
            request.Set();
            if (worker is { IsAlive: true }) worker.Join(500);
        }

        void Run()
        {
            var com = CoInitializeEx(0, COINIT_MULTITHREADED) >= 0;
            nint uia = 0;
            try
            {
                if (CoCreateInstance(in CLSID_CUIAutomation, 0, CLSCTX_INPROC_SERVER, in IID_IUIAutomation, out uia) < 0) uia = 0;
                while (!stopping)
                {
                    request.WaitOne();
                    if (stopping) break;
                    int ticket;
                    lock (answerGate) ticket = asked;
                    var result = false;
                    try { result = uia != 0 && FocusIsPassword(uia); }
                    catch { }
                    lock (answerGate)
                    {
                        // пока спрашивали, фокус сменился ещё раз — ответ устарел, спросим снова
                        if (ticket != asked) { request.Set(); continue; }
                        answered = ticket;
                        owner.SetPassword(result);
                        Monitor.PulseAll(answerGate);
                    }
                }
            }
            catch { }
            finally
            {
                if (uia != 0) Release(uia);
                if (com) CoUninitialize();
            }
        }

        static bool FocusIsPassword(nint uia)
        {
            nint element = 0;
            var vt = *(nint**)uia;
            var hr = ((delegate* unmanaged<nint, nint*, int>)vt[SlotGetFocusedElement])(uia, &element);
            if (hr < 0 || element == 0) return false;
            try
            {
                var v = new VARIANT();
                var evt = *(nint**)element;
                hr = ((delegate* unmanaged<nint, int, VARIANT*, int>)evt[SlotGetCurrentPropertyValue])(element, UIA_IsPasswordPropertyId, &v);
                if (hr < 0) return false;
                var isPassword = v.vt == VT_BOOL && (short)(v.data & 0xFFFF) != 0;
                VariantClear(ref v);
                return isPassword;
            }
            finally
            {
                Release(element);
            }
        }

        static void Release(nint unknown) => ((delegate* unmanaged<nint, uint>)(*(nint**)unknown)[SlotRelease])(unknown);
    }
}
