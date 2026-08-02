using IconicLauncher.Core.Utils;

namespace IconicLauncher.Tests;

public class MetaCppParserTests
{
    [Fact]
    public void ParsesStandardMetaCpp()
    {
        var content = "protocol = 4;\npublishedid = 3077736647;\nname = \"Iconic Server Pack Core\";\ntimestamp = 5248158538584469186;\n";
        var info = MetaCppParser.Parse(content);
        Assert.NotNull(info);
        Assert.Equal("3077736647", info.PublishedId);
        Assert.Equal("Iconic Server Pack Core", info.Name);
    }

    [Fact]
    public void ParsesTightWhitespace()
    {
        var content = "protocol=4;\npublishedid=1559212036;\nname=\"CF\";\n";
        var info = MetaCppParser.Parse(content);
        Assert.NotNull(info);
        Assert.Equal("1559212036", info.PublishedId);
        Assert.Equal("CF", info.Name);
    }

    [Fact]
    public void ParsesTabsAndCrLfAndExtraSpaces()
    {
        var content = "protocol \t=  4;\r\npublishedid \t =  2545327648 ;\r\nname \t=  \"Dabs Framework\" ;\r\n";
        var info = MetaCppParser.Parse(content);
        Assert.NotNull(info);
        Assert.Equal("2545327648", info.PublishedId);
        Assert.Equal("Dabs Framework", info.Name);
    }

    [Fact]
    public void ZeroPublishedIdReturnsNull()
    {
        var content = "protocol = 4;\npublishedid = 0;\nname = \"Local Unpublished Mod\";\n";
        Assert.Null(MetaCppParser.Parse(content));
    }

    [Fact]
    public void MissingPublishedIdReturnsNull()
    {
        var content = "protocol = 4;\nname = \"No Id Here\";\ntimestamp = 123;\n";
        Assert.Null(MetaCppParser.Parse(content));
    }

    [Fact]
    public void MissingNameStillParsesId()
    {
        var content = "protocol = 4;\npublishedid = 1644467354;\ntimestamp = 123;\n";
        var info = MetaCppParser.Parse(content);
        Assert.NotNull(info);
        Assert.Equal("1644467354", info.PublishedId);
    }

    [Fact]
    public void ParsesNameWithSpacesAndSpecialChars()
    {
        var content = "protocol = 4;\npublishedid = 1602372402;\nname = \"Munghard's Itempack v1.2 - [Community] & Friends\";\n";
        var info = MetaCppParser.Parse(content);
        Assert.NotNull(info);
        Assert.Equal("1602372402", info.PublishedId);
        Assert.Equal("Munghard's Itempack v1.2 - [Community] & Friends", info.Name);
    }
}
