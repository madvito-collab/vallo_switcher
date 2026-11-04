using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class StartProblemTests
{
    [Theory]
    [InlineData(StartProblem.NoData, "Свитчер не запущен: нет папки Data — распакуйте архив целиком")]
    [InlineData(StartProblem.NoLayouts, "Свитчер не запущен: нет раскладки Английская (США) или Русская")]
    [InlineData(StartProblem.NoHook, "Свитчер не запущен: Windows не дала перехватить клавиатуру")]
    [InlineData(StartProblem.Unknown, "Свитчер не запущен: непредвиденная ошибка")]
    public void MenuText(StartProblem problem, string text) =>
        Assert.Equal(text, StartProblems.MenuText(problem));
}

public class AutostartRuleTests
{
    [Theory]
    [InlineData(false, false, true)]  // первый запуск — включаем
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]    // включён — переписываем путь: exe могли перенести
    [InlineData(true, false, false)]  // человек выключил — не трогаем
    public void ShouldWrite(bool firstRunDone, bool runValueExists, bool expected) =>
        Assert.Equal(expected, AutostartRule.ShouldWrite(firstRunDone, runValueExists));
}
