using IconicLauncher.Core.Utils;
using Xunit;

namespace IconicLauncher.Tests;

public class ServerInputTests
{
    [Theory]
    [InlineData("193.25.252.68", null, null)]
    [InlineData("193.25.252.68:2302", 2302, null)]
    [InlineData("193.25.252.68:2302:2303", 2302, 2303)]
    [InlineData("  193.25.252.68 : 2302  ", 2302, null)]
    [InlineData("steam://connect/193.25.252.68:2302", 2302, null)]
    public void AddressSplitsIntoHostAndPorts(string input, int? expectedGame, int? expectedQuery)
    {
        Assert.True(ServerInput.TrySplitAddress(input, out var host, out var game, out var query));

        Assert.Equal("193.25.252.68", host);
        Assert.Equal(expectedGame, game);
        Assert.Equal(expectedQuery, query);
    }

    [Theory]
    [InlineData("play.example.com")]
    [InlineData("193.25.252.68")]
    public void RealAddressesValidate(string host)
    {
        Assert.True(ServerInput.IsValidHost(host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]
    [InlineData("bad host.com")]
    [InlineData(".example.com")]
    [InlineData("example..com")]
    [InlineData("http://example.com")]
    public void JunkAddressesAreRejected(string host)
    {
        Assert.False(ServerInput.IsValidHost(host));
    }

    [Theory]
    [InlineData("2302", true, 2302)]
    [InlineData("0", false, 0)]
    [InlineData("65536", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    public void PortsAreRangeChecked(string text, bool expected, int expectedPort)
    {
        Assert.Equal(expected, ServerInput.TryParsePort(text, out var port));
        Assert.Equal(expectedPort, port);
    }

    [Fact]
    public void CustomIdIsStableForTheSameAddress()
    {
        var first = ServerInput.BuildCustomId("193.25.252.68", 2302);
        var second = ServerInput.BuildCustomId("193.25.252.68", 2302);

        Assert.Equal(first, second);
        Assert.Equal("custom-193-25-252-68-2302", first);
        Assert.True(ServerInput.IsCustomId(first));
        Assert.NotEqual(first, ServerInput.BuildCustomId("193.25.252.68", 2402));
    }

    [Fact]
    public void ConfigIdsAreNotMistakenForCustomOnes()
    {
        Assert.False(ServerInput.IsCustomId("eumain"));
        Assert.False(ServerInput.IsCustomId(""));
        Assert.False(ServerInput.IsCustomId(null));
    }
}
