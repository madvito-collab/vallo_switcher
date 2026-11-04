using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// Выделенный текст — через Ctrl+C, замена — через Ctrl+V. Буфер обмена человека возвращаем как был.
/// Вызывается с потока-исполнителя (порядок с остальным вводом держит очередь). Сам буфер открываем
/// только с потока-«буфера»: у него окно-невидимка и цикл сообщений — без окна Windows не даст положить
/// данные, а без цикла другие программы зависнут, очищая буфер, владельцем которого мы стали.
sealed unsafe class SelectionReader : IDisposable
{
    const int CopyWaitMs = 300;
    /// Electron и Figma читают буфер поздно — раньше вернём исходное, вставится оно (на Маке тоже 0,7 с).
    const int RestoreDelayMs = 700;
    /// Сколько ждём один вызов на потоке «буфера». Завис — дальше не ждём вовсе (см. `stuck`):
    /// вся операция замирает не больше чем на ~1 с.
    const int CallWaitMs = 250;
    const int OpenTries = 10;
    const int OpenRetryMs = 10;
    /// Больше не копируем: огромная картинка в памяти дважды — дороже, чем потерять её копию.
    const long MaxSavedBytes = 64L << 20;

    const ushort VK_CONTROL = 0x11, VK_C = 0x43, VK_V = 0x56;

    /// Формат и его байты. Картинку CF_BITMAP система соберёт сама из CF_DIB, который мы сохраним.
    sealed record Saved(List<(uint Format, byte[] Data)> Formats);

    readonly InputSender sender;
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    readonly ConcurrentQueue<Action> calls = new();
    uint threadId;
    nint hwnd;
    /// Вызов, которого не дождались и который ещё не закончился: пока он висит, новые не ставим и не ждём.
    volatile Call? stuck;

    /// Один вызов на потоке «буфера». Ждавший ушёл по таймауту — вызов отменён и буфер уже не очищает:
    /// иначе он сотрёт буфер человека, когда сохранять его будет уже некому.
    sealed class Call
    {
        const int Pending = 0, Committed = 1, Cancelled = 2;
        int state;
        public readonly ManualResetEventSlim Done = new();
        public volatile bool Finished;

        public bool IsCancelled => Volatile.Read(ref state) == Cancelled;

        /// Поток «буфера», прямо перед EmptyClipboard. false — ждавший ушёл, буфер не трогаем.
        public bool Commit() => Interlocked.CompareExchange(ref state, Committed, Pending) != Cancelled;

        /// Ждавший, по таймауту. false — вызов уже начал менять буфер, его надо дождаться.
        public bool Cancel() => Interlocked.CompareExchange(ref state, Cancelled, Pending) != Committed;
    }

    readonly object restoreGate = new();
    /// Исходный буфер, который ещё надо вернуть после вставки.
    Saved? pendingRestore;
    /// Номер содержимого буфера сразу после нашей подмены: изменился — человек скопировал своё, не трогаем.
    uint ourSequence;
    Timer? restoreTimer;
    /// Номер подмены: таймер прошлой подмены, сработавший посреди новой, ничего не трогает.
    int generation;

    /// Наши записи в буфер не должны попасть в журнал буфера Windows (Win+V) и в облако — текст никуда не пишется.
    static readonly uint ExcludeFromMonitor = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint CanIncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    static readonly uint CanUploadToCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

    public SelectionReader(InputSender sender)
    {
        this.sender = sender;
        thread = new Thread(Run) { IsBackground = true, Name = "Vallo clipboard" };
        thread.Start();
        ready.Wait(2000);
    }

    public void Dispose()
    {
        CancelTimer();
        RestoreNow();
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, 0, 0);
        if (thread.IsAlive) thread.Join(1000);
    }

    /// Выделенный текст или null. Null — буфер уже возвращён. Не null — потом обязательно Paste или Restore.
    public string? Copy()
    {
        // прошлая подмена ещё висит — сперва вернуть исходное (если человек не скопировал своё), иначе сохраним свою же
        CancelTimer();
        RestoreCall(gen: null, onlyIfOurs: true);
        var saved = OnClipboard(call => Open() ? Use(() =>
        {
            var s = Save();
            if (!call.Commit()) return null;
            EmptyClipboard();
            return s;
        }) : null);
        if (saved is null) return null; // буфер занят — лучше ничего не делать, чем потерять чужое
        lock (restoreGate) pendingRestore = saved;

        var before = OnClipboard(_ => Open() ? Use(ReadText) : null); // после очистки — обычно пусто
        var sequence = GetClipboardSequenceNumber();
        if (!sender.Shortcut(VK_CONTROL, VK_C))
        {
            RestoreNow();
            return null;
        }
        // первая смена номера — ещё не текст (программа могла только очистить буфер или положить другой формат первым)
        var deadline = Environment.TickCount64 + CopyWaitMs;
        while (Environment.TickCount64 < deadline
               && !(GetClipboardSequenceNumber() != sequence && IsClipboardFormatAvailable(CF_UNICODETEXT)))
            Thread.Sleep(10);

        var after = GetClipboardSequenceNumber() == sequence ? null : OnClipboard(_ => Open() ? Use(ReadText) : null);
        if (SelectionDecision.Decide(before, after) is SelectionDecision.Result.Selected s) return s.Text;
        RestoreNow();
        return null;
    }

    /// Заменить выделение: текст в буфер, Ctrl+V, исходный буфер — обратно через 700 мс.
    public bool Paste(string text)
    {
        var put = OnClipboard(call => Open() && Use(() =>
        {
            if (!call.Commit()) return false;
            EmptyClipboard();
            var ok = SetText(text);
            MarkPrivate();
            return ok;
        }));
        if (!put)
        {
            RestoreNow();
            return false;
        }
        ScheduleRestore(GetClipboardSequenceNumber());
        return sender.Shortcut(VK_CONTROL, VK_V);
    }

    /// Вставлять нечего — вернуть исходный буфер сразу.
    public void Restore() => RestoreNow();

    void RestoreNow()
    {
        var sequence = GetClipboardSequenceNumber();
        RestoreCall(gen: null, onlyIfOurs: false);
        // не дождались потока «буфера» — исходное ещё у нас: вернём попозже, если буфер с тех пор не трогали
        bool left;
        lock (restoreGate) left = pendingRestore is not null;
        if (left) ScheduleRestore(sequence);
    }

    /// Вернуть исходный буфер через RestoreDelayMs, если номер содержимого всё ещё `sequence`.
    void ScheduleRestore(uint sequence)
    {
        lock (restoreGate)
        {
            ourSequence = sequence;
            restoreTimer?.Dispose();
            var gen = ++generation;
            restoreTimer = new Timer(_ => RestoreCall(gen, onlyIfOurs: true), null, RestoreDelayMs, Timeout.Infinite);
        }
    }

    void CancelTimer()
    {
        lock (restoreGate)
        {
            generation++;
            restoreTimer?.Dispose();
            restoreTimer = null;
        }
    }

    /// Вернуть исходный буфер. Всё решается на потоке «буфера», где вызовы идут строго по одному:
    /// опоздавший таймер (`gen` устарел) не вклинится между сохранением и Ctrl+C новой подмены.
    /// `onlyIfOurs` — после вставки: если человек успел скопировать своё, его новое важнее старого.
    void RestoreCall(int? gen, bool onlyIfOurs)
    {
        try
        {
            OnClipboard(call =>
            {
                lock (restoreGate)
                    if (pendingRestore is null || (gen is { } g0 && g0 != generation)) return false;
                return Open() && Use(() =>
                {
                    if (!call.Commit()) return false;
                    Saved? saved;
                    lock (restoreGate)
                    {
                        if (gen is { } g && g != generation) return false;
                        if (pendingRestore is null) return false;
                        // человек скопировал своё — его новое важнее старого
                        if (onlyIfOurs && GetClipboardSequenceNumber() != ourSequence)
                        {
                            pendingRestore = null;
                            return false;
                        }
                        saved = pendingRestore;
                        pendingRestore = null;
                    }
                    Put(saved);
                    MarkPrivate();
                    return true;
                });
            });
        }
        catch { }
    }

    // ---- Поток «буфера»: окно-невидимка и цикл сообщений

    void Run()
    {
        try
        {
            threadId = GetCurrentThreadId();
            // готовый системный класс STATIC — свой регистрировать незачем; HWND_MESSAGE — окно без экрана
            hwnd = CreateWindowEx(0, "STATIC", "Vallo clipboard", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, GetModuleHandle(null), 0);
        }
        finally
        {
            ready.Set();
        }
        if (hwnd == 0) return;
        try
        {
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.hwnd == 0 && msg.message == WM_APP)
                {
                    while (calls.TryDequeue(out var call))
                    {
                        try { call(); }
                        catch { } // исключение здесь уронило бы весь процесс
                    }
                    continue;
                }
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
        }
        finally
        {
            DestroyWindow(hwnd);
            hwnd = 0;
        }
    }

    /// Выполнить на потоке «буфера» и дождаться. Поток не отвечает — как будто буфер недоступен;
    /// брошенный вызов отменён и буфер не очистит (`Call.Commit` перед каждым EmptyClipboard).
    T? OnClipboard<T>(Func<Call, T> f)
    {
        if (hwnd == 0) return default;
        // прошлый брошенный вызов ещё висит — новый встанет за ним, ждать бесполезно
        if (stuck is { Finished: false }) return default;
        T? result = default;
        // без using: опоздавший вызов ещё дёрнет Set — событие должно быть живо
        var call = new Call();
        calls.Enqueue(() =>
        {
            try
            {
                if (!call.IsCancelled) result = f(call);
            }
            catch { }
            finally
            {
                call.Finished = true;
                call.Done.Set();
            }
        });
        if (!PostThreadMessage(threadId, WM_APP, 0, 0))
        {
            call.Cancel();
            return default;
        }
        if (call.Done.Wait(CallWaitMs)) return result;
        // уже меняет буфер — отменять поздно, остались миллисекунды: дождаться
        if (!call.Cancel() && call.Done.Wait(CallWaitMs)) return result;
        stuck = call;
        return default;
    }

    /// Буфер держит другая программа — пробуем ещё немного.
    bool Open()
    {
        for (var i = 0; i < OpenTries; i++)
        {
            if (OpenClipboard(hwnd)) return true;
            Thread.Sleep(OpenRetryMs);
        }
        return false;
    }

    static T Use<T>(Func<T> f)
    {
        try { return f(); }
        finally { CloseClipboard(); }
    }

    /// Все форматы, которые лежат в памяти (HGLOBAL). Картинки-дескрипторы (CF_BITMAP, метафайлы, палитра),
    /// «рисует владелец» и частные форматы — не память, их не скопировать; CF_DIB остаётся, картинка вернётся.
    static Saved Save()
    {
        var list = new List<(uint, byte[])>();
        long total = 0;
        uint format = 0;
        while ((format = EnumClipboardFormats(format)) != 0)
        {
            if (!IsMemoryFormat(format)) continue;
            var h = GetClipboardData(format);
            if (h == 0) continue;
            var size = (long)GlobalSize(h);
            if (size <= 0 || total + size > MaxSavedBytes) continue;
            var p = GlobalLock(h);
            if (p == null) continue;
            try
            {
                var bytes = new byte[size];
                Marshal.Copy((nint)p, bytes, 0, (int)size);
                list.Add((format, bytes));
                total += size;
            }
            finally
            {
                GlobalUnlock(h);
            }
        }
        return new Saved(list);
    }

    static bool IsMemoryFormat(uint f) => f switch
    {
        2 or 3 or 9 or 14 => false,               // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE, CF_ENHMETAFILE
        0x80 or 0x82 or 0x83 or 0x8E => false,    // CF_OWNERDISPLAY, CF_DSPBITMAP, CF_DSPMETAFILEPICT, CF_DSPENHMETAFILE
        >= 0x200 and <= 0x3FF => false,           // CF_PRIVATEFIRST…CF_GDIOBJLAST: не память
        _ => true,
    };

    static void Put(Saved saved)
    {
        EmptyClipboard();
        foreach (var (format, data) in saved.Formats) SetBytes(format, data);
    }

    /// Не в журнал Win+V и не в облако. Вызывать при открытом буфере, после своих данных.
    static void MarkPrivate()
    {
        var zero = new byte[4]; // DWORD 0
        if (ExcludeFromMonitor != 0) SetBytes(ExcludeFromMonitor, zero);
        if (CanIncludeInHistory != 0) SetBytes(CanIncludeInHistory, zero);
        if (CanUploadToCloud != 0) SetBytes(CanUploadToCloud, zero);
    }

    static bool SetText(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2]; // с нулём на конце
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return SetBytes(CF_UNICODETEXT, bytes);
    }

    static bool SetBytes(uint format, byte[] data)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
        if (h == 0) return false;
        var p = GlobalLock(h);
        if (p == null)
        {
            GlobalFree(h);
            return false;
        }
        Marshal.Copy(data, 0, (nint)p, data.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) != 0) return true; // теперь память принадлежит системе
        GlobalFree(h);
        return false;
    }

    static string? ReadText()
    {
        var h = GetClipboardData(CF_UNICODETEXT);
        if (h == 0) return null;
        var size = (long)GlobalSize(h);
        var p = (char*)GlobalLock(h);
        if (p == null) return null;
        try
        {
            var max = (int)(size / 2);
            var len = 0;
            while (len < max && p[len] != '\0') len++;
            return new string(p, 0, len);
        }
        finally
        {
            GlobalUnlock(h);
        }
    }
}
