using System.Collections.Concurrent;

namespace Vallo.Core;

/// Журнал «Режима отладки» на Windows (на Маке — системный журнал). Файл debug.log рядом с настройками.
/// Пишем только что и когда случилось (вид действия, длины, имя программы), сам текст человека — никогда.
/// `Write` быстрый (в очередь): его зовут из перехвата клавиатуры; в файл пишет фоновый поток.
/// Больше `MaxBytes` — старое уходит в debug.old.log, новый файл с нуля.
public sealed class DebugLog : IDisposable
{
    public const long MaxBytes = 512 * 1024;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vallo Switcher", "debug.log");

    /// Общий журнал приложения; в тестах свой.
    public static DebugLog Shared { get; } = new(DefaultPath);

    readonly string path;
    readonly BlockingCollection<string> queue = new(boundedCapacity: 10_000);
    readonly Thread writer;
    volatile bool enabled;

    public DebugLog(string path)
    {
        this.path = path;
        writer = new Thread(WriteLoop) { IsBackground = true, Name = "Vallo debug log" };
        writer.Start();
    }

    public string FilePath => path;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            if (!value) Add("журнал выключен");
            enabled = value;
            if (value) Add("журнал включён");
        }
    }

    /// Строка в журнал. Выключен — ничего (и строку не собираем: передавайте готовую или через `Write(Func)`).
    public void Write(string message)
    {
        if (enabled) Add(message);
    }

    /// Для строк, которые дорого собирать: соберём, только если журнал включён.
    public void Write(Func<string> message)
    {
        if (enabled) Add(message());
    }

    void Add(string message) =>
        queue.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}");

    /// Дождаться, пока всё из очереди ляжет в файл (тесты, кнопка «Открыть журнал»).
    public void Flush(TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while ((queue.Count > 0 || Volatile.Read(ref writing) != 0) && DateTime.UtcNow < until) Thread.Sleep(5);
    }

    int writing;

    void WriteLoop()
    {
        foreach (var line in queue.GetConsumingEnumerable())
        {
            Interlocked.Exchange(ref writing, 1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var lines = new List<string> { line };
                while (queue.TryTake(out var more)) lines.Add(more);
                Rotate();
                File.AppendAllLines(path, lines);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* файл занят — эти строки теряем */ }
            finally { Interlocked.Exchange(ref writing, 0); }
        }
    }

    void Rotate()
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes) return;
        File.Move(path, OldPath, overwrite: true);
    }

    public string OldPath => Path.Combine(Path.GetDirectoryName(path)!, "debug.old.log");

    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(1));
    }
}

/// Как действия движка выглядят в журнале: без текста — только виды и длины.
public static class LogText
{
    public static string Summary(IEnumerable<EngineAction> actions) => string.Join(" ", actions.Select(a => a switch
    {
        EngineAction.Delete d => $"delete({d.N})",
        EngineAction.Insert i => $"insert({TextElements.Count(i.Text)} chars)",
        EngineAction.SwitchLayout s => $"switch({s.L.Code()})",
        EngineAction.ReplaceSelection r => $"replaceSelection({TextElements.Count(r.Text)} chars)",
        EngineAction.Learn => "learn",
        EngineAction.PressKey p => $"key(0x{p.Vk:X2})",
        _ => "?",
    }));
}
