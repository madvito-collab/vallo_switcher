using System.IO.Compression;
using System.Security.Cryptography;


namespace Vallo.Core;

/// Скачивание, проверка, распаковка и удаление модели запятых — как `ModelStore.swift` на Маке.
/// Качает в фоне; `Changed` зовётся из фона — кто показывает, сам переходит на свой поток.
public sealed class ModelStore : IDisposable
{
    readonly string directory;
    readonly object gate = new();
    CancellationTokenSource? cancel;
    ModelState state;

    /// Состояние поменялось (процент, готово, ошибка). Из любого потока.
    public Action? Changed { get; set; }
    /// Модель удалили или заменили — сервису запятых выгрузить старую. Из любого потока.
    public Action? ModelReplaced { get; set; }

    public ModelStore(string? directory = null)
    {
        this.directory = directory ?? CommaModelInfo.Directory;
        state = CommaModelInfo.IsInstalled(this.directory) ? new ModelState.Ready() : new ModelState.NotDownloaded();
    }

    public string Directory => directory;

    public ModelState State
    {
        get { lock (gate) return state; }
    }

    void Set(ModelState s)
    {
        lock (gate) state = s;
        Changed?.Invoke();
    }

    /// «Скачать» и «Повторить».
    public void Download()
    {
        CancellationTokenSource token;
        lock (gate)
        {
            if (state is ModelState.Downloading) return;
            cancel?.Dispose();
            cancel = token = new CancellationTokenSource();
            state = new ModelState.Downloading(0);
        }
        Changed?.Invoke();
        DebugLog.Shared.Write("модель запятых: скачиваем");
        _ = Task.Run(() => Run(token.Token));
    }

    /// «Отмена».
    public void Cancel()
    {
        lock (gate) cancel?.Cancel();
    }

    /// Корзина. Модель выгружаем, файлы удаляем.
    public void Delete()
    {
        Cancel();
        ModelReplaced?.Invoke();
        try { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DebugLog.Shared.Write(() => $"модель запятых: удалить не вышло — {e.GetType().Name}");
        }
        DebugLog.Shared.Write("модель запятых: удалена");
        Set(new ModelState.NotDownloaded());
    }

    /// Модель не загрузилась (битые файлы) — предложить скачать заново.
    public void MarkBroken()
    {
        // только если модель считалась готовой: во время удаления или скачивания это не поломка
        if (State is not ModelState.Ready) return;
        try { System.IO.Directory.Delete(directory, recursive: true); } catch (Exception) { }
        DebugLog.Shared.Write("модель запятых: файлы повреждены");
        Set(new ModelState.Failed(CommaModelInfo.Broken));
    }

    async Task Run(CancellationToken token)
    {
        var zip = Path.Combine(Path.GetTempPath(), $"vallo-model-{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ValloSwitcher");
                using var response = await http.GetAsync(CommaModelInfo.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Set(new ModelState.Failed(CommaModelInfo.ServerError((int)response.StatusCode)));
                    return;
                }
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var file = File.Create(zip);
                var buffer = new byte[1 << 16];
                long done = 0;
                var shown = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    if (total <= 0) continue;
                    // показываем только новые проценты — страница не перерисовывается на каждые 64 КБ
                    var percent = (int)(done * 100 / total);
                    if (percent == shown) continue;
                    shown = percent;
                    Set(new ModelState.Downloading(Math.Min(0.99, done / (double)total)));
                }
            }
            Set(new ModelState.Downloading(1));
            Install(zip);
            DebugLog.Shared.Write("модель запятых: скачана и распакована");
            ModelReplaced?.Invoke();
            Set(new ModelState.Ready());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            DebugLog.Shared.Write("модель запятых: скачивание отменили");
            Set(CommaModelInfo.IsInstalled(directory) ? new ModelState.Ready() : new ModelState.NotDownloaded());
        }
        catch (InstallException e)
        {
            DebugLog.Shared.Write(() => $"модель запятых: {e.Message}");
            Set(new ModelState.Failed(e.Message));
        }
        catch (Exception e)
        {
            DebugLog.Shared.Write(() => $"модель запятых: не скачалась — {e.GetType().Name}");
            Set(new ModelState.Failed(CommaModelInfo.NoNetwork));
        }
        finally
        {
            try { File.Delete(zip); } catch (Exception) { }
        }
    }

    sealed class InstallException(string message) : Exception(message);

    /// Сверить контрольную сумму, распаковать во временную папку и только потом заменить модель.
    void Install(string zip)
    {
        string hash;
        using (var stream = File.OpenRead(zip)) hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (hash != CommaModelInfo.Sha256) throw new InstallException(CommaModelInfo.BadChecksum);
        var models = Path.GetDirectoryName(directory)!;
        System.IO.Directory.CreateDirectory(models);
        var temp = Path.Combine(models, ".unpack-" + Guid.NewGuid().ToString("N"));
        try
        {
            try { ZipFile.ExtractToDirectory(zip, temp); }
            catch (Exception e) when (e is InvalidDataException or IOException) { throw new InstallException(CommaModelInfo.UnpackFailed); }
            var unpacked = Path.Combine(temp, CommaModelInfo.Name);
            if (!CommaModelInfo.IsInstalled(unpacked)) throw new InstallException(CommaModelInfo.UnpackFailed);
            // старую модель — выгрузить и убрать, новую — на её место
            ModelReplaced?.Invoke();
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
            System.IO.Directory.Move(unpacked, directory);
        }
        finally
        {
            try { if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, recursive: true); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = null;
        }
    }
}
