using System.Text;

namespace Vallo.Core;

/// Запятые по сочетанию, как `CommaService.swift` на Маке: держит модель (загружает лениво), режет текст на предложения,
/// прогоняет через модель в фоне и фильтрует ответ. Без модели, при ошибке или дольше таймаута — null.
public sealed class CommaService : IDisposable
{
    /// Ожидание ответа модели: 3 с, если она уже в памяти; при первом запуске — ещё и на загрузку.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ColdTimeout = TimeSpan.FromSeconds(8);
    /// Модель занимает ~500 МБ памяти — выгружаем, если ею не пользовались полчаса.
    public static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(30);

    readonly string directory;
    readonly TimeSpan idleUnload;
    readonly object gate = new();
    readonly SemaphoreSlim loading = new(1, 1);
    readonly Timer idleTimer;
    CommaModel? model;
    /// Сколько прогонов идёт прямо сейчас: выгружаем модель, только когда последний закончился.
    int inUse;
    bool unloadPending;

    /// Модель есть на диске, но не загрузилась (битые файлы). Из фона.
    public Action? LoadFailed { get; set; }

    public CommaService(string? directory = null, TimeSpan? idleUnload = null)
    {
        this.directory = directory ?? CommaModelInfo.Directory;
        this.idleUnload = idleUnload ?? IdleUnload;
        idleTimer = new Timer(_ => Unload(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    public bool IsInstalled => CommaModelInfo.IsInstalled(directory);

    public bool IsLoaded
    {
        get { lock (gate) return model is not null; }
    }

    /// Модель удалили или заменили — выгрузить (как только закончатся идущие прогоны).
    public void Unload()
    {
        CommaModel? toDispose = null;
        lock (gate)
        {
            if (model is null) return;
            if (inUse > 0) { unloadPending = true; return; }
            toDispose = model;
            model = null;
        }
        toDispose.Dispose();
        DebugLog.Shared.Write("запятые: модель выгружена");
    }

    /// Текст с запятыми (только безопасные правки) или null.
    public async Task<string?> FixAsync(string text, TypoCorrector typos, bool capitalize)
    {
        if (!IsInstalled) return null;
        var timeout = IsLoaded ? Timeout : ColdTimeout;
        var work = Task.Run(() => Fix(text, typos, capitalize));
        var finished = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != work)
        {
            DebugLog.Shared.Write(() => $"запятые: модель не успела за {timeout.TotalSeconds:0} с — без запятых");
            return null;
        }
        return await work.ConfigureAwait(false);
    }

    string? Fix(string text, TypoCorrector typos, bool capitalize)
    {
        idleTimer.Change(idleUnload, System.Threading.Timeout.InfiniteTimeSpan);
        if (Acquire() is not { } m) return null;
        try
        {
            var lines = text.Split('\n').Select(line =>
            {
                // пустые строки и строки без букв модели не нужны
                if (!line.Any(char.IsLetter)) return line;
                return string.Join(" ", Sentences(line).Select(sentence =>
                {
                    try { return CommaFilter.Merge(sentence, m.Run(sentence), typos, capitalize); }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        DebugLog.Shared.Write(() => $"запятые: модель упала — {e.GetType().Name}");
                        return sentence;
                    }
                }));
            });
            return string.Join("\n", lines);
        }
        finally { Release(); }
    }

    /// Модель для прогона (загрузить, если надо). null — не загрузилась.
    CommaModel? Acquire()
    {
        lock (gate)
        {
            if (model is not null) { inUse++; unloadPending = false; return model; }
        }
        loading.Wait();
        try
        {
            lock (gate)
            {
                if (model is not null) { inUse++; unloadPending = false; return model; }
            }
            CommaModel loaded;
            try { loaded = CommaModel.Load(directory); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                DebugLog.Shared.Write(() => $"запятые: модель не загрузилась — {e.GetType().Name}");
                LoadFailed?.Invoke();
                return null;
            }
            DebugLog.Shared.Write("запятые: модель загружена");
            lock (gate)
            {
                model = loaded;
                inUse++;
                return loaded;
            }
        }
        finally { loading.Release(); }
    }

    void Release()
    {
        bool unload;
        lock (gate)
        {
            inUse--;
            unload = inUse == 0 && unloadPending;
            if (unload) unloadPending = false;
        }
        if (unload) Unload();
    }

    /// Строку — на предложения (после . ! ? … и пробела), чтобы модель не обрезала длинный ответ.
    public static List<string> Sentences(string line)
    {
        if (line.Trim(' ').Length == 0) return [line];
        var result = new List<string>();
        var current = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            var ch = line[i++];
            current.Append(ch);
            if (".!?…".Contains(ch) && i < line.Length && line[i] == ' ')
            {
                result.Add(current.ToString());
                current.Clear();
                while (i < line.Length && line[i] == ' ') i++;
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    public void Dispose()
    {
        idleTimer.Dispose();
        lock (gate)
        {
            model?.Dispose();
            model = null;
        }
        loading.Dispose();
    }
}
