using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class ExclusionMatcherTests
{
    [Fact]
    public void MatchIgnoresCase() =>
        Assert.True(ExclusionMatcher.IsExcluded("JAVAW.EXE", ["javaw.exe"]));

    [Fact]
    public void OtherAppIsNotExcluded() =>
        Assert.False(ExclusionMatcher.IsExcluded("telegram.exe", ["javaw.exe"]));

    [Fact]
    public void FullPathMatchesByFileName() =>
        Assert.True(ExclusionMatcher.IsExcluded(@"C:\Program Files\Java\bin\javaw.exe", ["javaw.exe"]));
}
