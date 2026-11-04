namespace Vallo.Core;

/// Почему свитчер не запустился — человеку в меню трея.
public enum StartProblem { NoData, NoLayouts, NoHook, Unknown }

public static class StartProblems
{
    public static string MenuText(StartProblem problem) => "Свитчер не запущен: " + problem switch
    {
        StartProblem.NoData => "нет папки Data — распакуйте архив целиком",
        StartProblem.NoLayouts => "нет раскладки Английская (США) или Русская",
        StartProblem.NoHook => "Windows не дала перехватить клавиатуру",
        _ => "непредвиденная ошибка",
    };
}

/// Автозапуск: при первом запуске включаем; пока включён — переписываем путь к exe на каждом старте
/// (папку с программой могли перенести). Выключил человек — не трогаем.
public static class AutostartRule
{
    public static bool ShouldWrite(bool firstRunDone, bool runValueExists) => !firstRunDone || runValueExists;
}
