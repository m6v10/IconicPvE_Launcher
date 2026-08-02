using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class ModListBuilderTests
{
    [Fact]
    public void JoinsMultiplePathsWithSemicolons()
    {
        var paths = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\1559212036",
            @"C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\3077736647",
            @"D:\SteamLibrary\steamapps\workshop\content\221100\1644467354"
        };
        var result = ModListBuilder.BuildModArgument(paths);
        Assert.Equal(@"-mod=C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\1559212036;C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\3077736647;D:\SteamLibrary\steamapps\workshop\content\221100\1644467354", result);
    }

    [Fact]
    public void SinglePathHasPrefixAndNoTrailingSemicolon()
    {
        var result = ModListBuilder.BuildModArgument(new[] { @"C:\Games\DayZ\!Workshop\@CF" });
        Assert.Equal(@"-mod=C:\Games\DayZ\!Workshop\@CF", result);
        Assert.StartsWith("-mod=", result);
        Assert.False(result.EndsWith(";"));
    }

    [Fact]
    public void ResultHasNoSurroundingQuotes()
    {
        var result = ModListBuilder.BuildModArgument(new[]
        {
            @"C:\Path With Spaces\@Mod One",
            @"C:\Path With Spaces\@Mod Two"
        });
        Assert.Equal(@"-mod=C:\Path With Spaces\@Mod One;C:\Path With Spaces\@Mod Two", result);
        Assert.DoesNotContain("\"", result);
    }
}
