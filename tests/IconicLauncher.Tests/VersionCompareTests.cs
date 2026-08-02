using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class VersionCompareTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("1.0.0", "garbage", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData("1.0.0", "", false)]
    [InlineData("1.0.0.1", "1.0.0", true)]
    public void IsNewerComparesVersions(string latest, string current, bool expected)
    {
        Assert.Equal(expected, SelfUpdateService.IsNewer(latest, current));
    }
}
