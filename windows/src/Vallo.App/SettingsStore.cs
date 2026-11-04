using Vallo.Core;

namespace Vallo.App;

/// Текущие настройки в памяти + запись на диск при каждом изменении.
/// Меняют из разных потоков (трей, исполнитель свитчера) — изменения по одному.
sealed class SettingsStore(string path)
{
    readonly object gate = new();
    volatile AppSettings current = AppSettings.Load(path);

    public AppSettings Current => current;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        lock (gate)
        {
            current = change(current);
            try
            {
                current.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // не записалось — живём с настройками в памяти, в следующий раз попробуем снова
            }
        }
    }
}
