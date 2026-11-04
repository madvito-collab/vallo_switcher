namespace Vallo.App;

/// Один экземпляр на сеанс пользователя: именованный мьютекс.
static class SingleInstance
{
    const string Name = @"Local\ValloSwitcher";

    /// null — уже запущен другой экземпляр.
    public static Mutex? TryAcquire()
    {
        var m = new Mutex(initiallyOwned: true, Name, out var createdNew);
        if (createdNew) return m;
        m.Dispose();
        return null;
    }
}
