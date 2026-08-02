using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class A2SRulesParserTests
{
    [Fact]
    public void UnescapeMapsEscapeOneToOne()
    {
        var result = A2SRulesQueryService.UnescapePayload([0x01, 0x01]);
        Assert.Equal(new byte[] { 0x01 }, result);
    }

    [Fact]
    public void UnescapeMapsEscapeTwoToZero()
    {
        var result = A2SRulesQueryService.UnescapePayload([0x01, 0x02]);
        Assert.Equal(new byte[] { 0x00 }, result);
    }

    [Fact]
    public void UnescapeMapsEscapeThreeToFf()
    {
        var result = A2SRulesQueryService.UnescapePayload([0x01, 0x03]);
        Assert.Equal(new byte[] { 0xFF }, result);
    }

    [Fact]
    public void UnescapeKeepsPlainBytes()
    {
        var input = new byte[] { 0x05, 0x7F, 0xAB, 0x02, 0x03 };
        var result = A2SRulesQueryService.UnescapePayload(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void UnescapeKeepsLeadByteBeforeNonEscapeByte()
    {
        var result = A2SRulesQueryService.UnescapePayload([0x01, 0x55, 0x01, 0x01]);
        Assert.Equal(new byte[] { 0x01, 0x55, 0x01 }, result);
    }

    [Fact]
    public void UnescapeKeepsTrailingLeadByteAtBufferEnd()
    {
        var result = A2SRulesQueryService.UnescapePayload([0x44, 0x01]);
        Assert.Equal(new byte[] { 0x44, 0x01 }, result);
    }

    [Fact]
    public void UnescapeHandlesMixedSequences()
    {
        var input = new byte[] { 0x10, 0x01, 0x02, 0x01, 0x03, 0x01, 0x01, 0x20 };
        var result = A2SRulesQueryService.UnescapePayload(input);
        Assert.Equal(new byte[] { 0x10, 0x00, 0xFF, 0x01, 0x20 }, result);
    }

    [Fact]
    public void UnescapeEmptyInputReturnsEmpty()
    {
        Assert.Empty(A2SRulesQueryService.UnescapePayload([]));
    }

    [Fact]
    public void ParseModListReadsTwoMods()
    {
        var payload = BuildPayload(2, 0, 0,
            [
                (0x11223344u, 1559212036uL, 4, "CF"),
                (0xAABBCCDDu, 1828439124uL, 4, "VPPAdminTools")
            ]);
        var mods = A2SRulesQueryService.ParseModList(payload);
        Assert.Equal(2, mods.Count);
        Assert.Equal("1559212036", mods[0].WorkshopId);
        Assert.Equal("CF", mods[0].Name);
        Assert.Equal("1828439124", mods[1].WorkshopId);
        Assert.Equal("VPPAdminTools", mods[1].Name);
    }

    [Fact]
    public void ParseModListReadsEightByteId()
    {
        var payload = BuildPayload(2, 0, 0, [(1u, 9876543210uL, 8, "BigId")]);
        var mods = A2SRulesQueryService.ParseModList(payload);
        Assert.Single(mods);
        Assert.Equal("9876543210", mods[0].WorkshopId);
    }

    [Fact]
    public void ParseModListSkipsDlcHashes()
    {
        var payload = BuildPayload(2, 0, 0x0003, [(5u, 123456uL, 4, "AfterDlc")]);
        var mods = A2SRulesQueryService.ParseModList(payload);
        Assert.Single(mods);
        Assert.Equal("123456", mods[0].WorkshopId);
        Assert.Equal("AfterDlc", mods[0].Name);
    }

    [Fact]
    public void ParseModListEmptyInputReturnsEmpty()
    {
        Assert.Empty(A2SRulesQueryService.ParseModList([]));
    }

    [Fact]
    public void ParseModListWrongVersionReturnsEmpty()
    {
        Assert.Empty(A2SRulesQueryService.ParseModList([9, 0, 0, 0, 1]));
    }

    [Fact]
    public void ParseModListTruncatedNameReturnsPartial()
    {
        var payload = BuildPayload(2, 0, 0,
            [
                (1u, 111uL, 4, "First"),
                (2u, 222uL, 4, "Second")
            ]);
        var truncated = payload[..(payload.Length - 3)];
        var mods = A2SRulesQueryService.ParseModList(truncated);
        Assert.Single(mods);
        Assert.Equal("111", mods[0].WorkshopId);
    }

    [Fact]
    public void ParseModListTruncatedMidHashReturnsPartial()
    {
        var payload = BuildPayload(2, 0, 0,
            [
                (1u, 111uL, 4, "First"),
                (2u, 222uL, 4, "Second")
            ]);
        var cut = payload[..(payload.Length - 2 - "Second".Length - 4 - 1 - 2)];
        var mods = A2SRulesQueryService.ParseModList(cut);
        Assert.Single(mods);
    }

    [Fact]
    public void ParseModListUnknownIdLengthStopsWithoutThrowing()
    {
        var payload = new List<byte> { 2, 0, 0, 0, 1, 0xAA, 0xBB, 0xCC, 0xDD, 7 };
        var mods = A2SRulesQueryService.ParseModList([.. payload]);
        Assert.Empty(mods);
    }

    [Fact]
    public void ParseRulesPacketsReadsPairsFromFullDatagram()
    {
        var response = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x45, 2, 0 };
        response.AddRange([0x01, 0x02, 0x00]);
        response.AddRange([0x41, 0x42, 0x00]);
        response.AddRange([0x02, 0x02, 0x00]);
        response.AddRange([0x43, 0x00]);
        var pairs = A2SRulesQueryService.ParseRulesPackets([.. response]);
        Assert.Equal(2, pairs.Count);
        Assert.Equal(new byte[] { 0x01, 0x02 }, pairs[0].Key);
        Assert.Equal(new byte[] { 0x41, 0x42 }, pairs[0].Value);
        Assert.Equal(new byte[] { 0x02, 0x02 }, pairs[1].Key);
        Assert.Equal(new byte[] { 0x43 }, pairs[1].Value);
    }

    [Fact]
    public void ParseRulesPacketsMissingTerminatorReturnsPartial()
    {
        var response = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x45, 2, 0 };
        response.AddRange([0x01, 0x02, 0x00]);
        response.AddRange([0x41, 0x00]);
        response.AddRange([0x02, 0x02]);
        var pairs = A2SRulesQueryService.ParseRulesPackets([.. response]);
        Assert.Single(pairs);
    }

    [Fact]
    public void ParseRulesPacketsEmptyReturnsEmpty()
    {
        Assert.Empty(A2SRulesQueryService.ParseRulesPackets([]));
    }

    [Fact]
    public void AssembleModPayloadOrdersPagesAndUnescapes()
    {
        var pairs = new List<KeyValuePair<byte[], byte[]>>
        {
            new([0x02, 0x02], [0x01, 0x03, 0x30]),
            new([0x01, 0x02], [0x10, 0x01, 0x02]),
            new([(byte)'k', (byte)'e', (byte)'y', 0x31], [0x99])
        };
        var assembled = A2SRulesQueryService.AssembleModPayload(pairs);
        Assert.Equal(new byte[] { 0x10, 0x00, 0xFF, 0x30 }, assembled);
    }

    [Fact]
    public void AssembleModPayloadNoPagesReturnsEmpty()
    {
        var pairs = new List<KeyValuePair<byte[], byte[]>>
        {
            new([(byte)'a', (byte)'b', (byte)'c'], [0x01])
        };
        Assert.Empty(A2SRulesQueryService.AssembleModPayload(pairs));
    }

    private static byte[] BuildPayload(byte version, byte flags, ushort dlcMask, (uint Hash, ulong Id, byte IdLen, string Name)[] mods)
    {
        var bytes = new List<byte> { version, flags };
        bytes.AddRange(BitConverter.GetBytes(dlcMask));
        var dlcBits = System.Numerics.BitOperations.PopCount(dlcMask);
        for (var i = 0; i < dlcBits; i++)
        {
            bytes.AddRange(BitConverter.GetBytes(0xDEADBEEFu));
        }
        bytes.Add((byte)mods.Length);
        foreach (var mod in mods)
        {
            bytes.AddRange(BitConverter.GetBytes(mod.Hash));
            bytes.Add(mod.IdLen);
            switch (mod.IdLen)
            {
                case 1:
                    bytes.Add((byte)mod.Id);
                    break;
                case 4:
                    bytes.AddRange(BitConverter.GetBytes((uint)mod.Id));
                    break;
                case 8:
                    bytes.AddRange(BitConverter.GetBytes(mod.Id));
                    break;
            }
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(mod.Name);
            bytes.Add((byte)nameBytes.Length);
            bytes.AddRange(nameBytes);
        }
        return [.. bytes];
    }
}
