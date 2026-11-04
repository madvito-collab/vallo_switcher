using Vallo.Core;

namespace Vallo.App;

/// Текущие настройки в памяти + запись на диск при каждом изменении.
sealed class SettingsStore(string path)
{
    public AppSettings Current { get; private set; } = AppSettings.Load(path);

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        try
        {
            Current.Save(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // не записалось — живём с настройками в памяти, в следующий раз попробуем снова
        }
    }
}
