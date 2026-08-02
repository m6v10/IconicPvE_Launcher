using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class A2SRulesQueryService : IServerModQueryService
{
    private const int TimeoutMs = 2500;
    private const byte RulesRequestByte = 0x56;
    private const byte ChallengeResponseByte = 0x41;
    private const byte RulesResponseByte = 0x45;
    private const uint MultiPacketHeader = 0xFFFFFFFE;

    public async Task<IReadOnlyList<ModEntry>?> QueryModListAsync(string ip, int queryPort, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var mods = await QueryOnceAsync(ip, queryPort, ct).ConfigureAwait(false);
                if (mods is not null)
                {
                    return mods;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("A2S_RULES attempt {Attempt} failed for {Ip}:{Port}: {Message}", attempt + 1, ip, queryPort, ex.Message);
            }
            if (ct.IsCancellationRequested)
            {
                break;
            }
        }
        Log.Debug("A2S_RULES query gave no mod list for {Ip}:{Port}", ip, queryPort);
        return null;
    }

    private static async Task<IReadOnlyList<ModEntry>?> QueryOnceAsync(string ip, int port, CancellationToken outerCt)
    {
        var data = await FetchRulesResponseAsync(ip, port, outerCt).ConfigureAwait(false);
        if (data is null)
        {
            return null;
        }
        var pairs = ParseRulesPackets(data);
        if (pairs.Count == 0)
        {
            return null;
        }
        var assembled = AssembleModPayload(pairs);
        if (assembled.Length < 4 || (assembled[0] != 2 && assembled[0] != 3))
        {
            return null;
        }
        return ParseModList(assembled);
    }

    public static async Task<byte[]?> FetchRulesResponseAsync(string ip, int port, CancellationToken outerCt)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        timeoutCts.CancelAfter(TimeoutMs);
        var token = timeoutCts.Token;
        using var udp = new UdpClient();
        udp.Connect(ip, port);
        var request = BuildRulesRequest(0xFF, 0xFF, 0xFF, 0xFF);
        await udp.SendAsync(request, token).ConfigureAwait(false);
        var data = (await udp.ReceiveAsync(token).ConfigureAwait(false)).Buffer;
        for (var round = 0; round < 3 && data.Length >= 9 && data[4] == ChallengeResponseByte; round++)
        {
            var challenged = BuildRulesRequest(data[5], data[6], data[7], data[8]);
            await udp.SendAsync(challenged, token).ConfigureAwait(false);
            data = (await udp.ReceiveAsync(token).ConfigureAwait(false)).Buffer;
        }
        if (data.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(data) == MultiPacketHeader)
        {
            data = await AssembleSplitPacketsAsync(udp, data, token).ConfigureAwait(false);
        }
        if (data is null || data.Length < 7 || data[4] != RulesResponseByte)
        {
            return null;
        }
        return data;
    }

    private static async Task<byte[]?> AssembleSplitPacketsAsync(UdpClient udp, byte[] first, CancellationToken token)
    {
        var id = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(4));
        if ((id & 0x80000000u) != 0)
        {
            return null;
        }
        int total = first[8];
        if (total <= 0 || total > 32)
        {
            return null;
        }
        var parts = new Dictionary<int, byte[]>();
        StoreSplitPart(parts, first, total);
        while (parts.Count < total)
        {
            var packet = (await udp.ReceiveAsync(token).ConfigureAwait(false)).Buffer;
            if (packet.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(packet) != MultiPacketHeader)
            {
                continue;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)) != id)
            {
                continue;
            }
            StoreSplitPart(parts, packet, total);
        }
        var size = 0;
        for (var i = 0; i < total; i++)
        {
            if (!parts.TryGetValue(i, out var part))
            {
                return null;
            }
            size += part.Length;
        }
        var assembled = new byte[size];
        var offset = 0;
        for (var i = 0; i < total; i++)
        {
            parts[i].CopyTo(assembled, offset);
            offset += parts[i].Length;
        }
        return assembled;
    }

    private static void StoreSplitPart(Dictionary<int, byte[]> parts, byte[] packet, int total)
    {
        int number = packet[9];
        if (number >= total || parts.ContainsKey(number))
        {
            return;
        }
        parts[number] = packet[12..];
    }

    private static byte[] BuildRulesRequest(byte c0, byte c1, byte c2, byte c3)
    {
        return [0xFF, 0xFF, 0xFF, 0xFF, RulesRequestByte, c0, c1, c2, c3];
    }

    public static byte[] UnescapePayload(byte[] raw)
    {
        var buffer = new byte[raw.Length];
        var length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == 0x01 && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if (next == 0x01)
                {
                    buffer[length++] = 0x01;
                    i++;
                    continue;
                }
                if (next == 0x02)
                {
                    buffer[length++] = 0x00;
                    i++;
                    continue;
                }
                if (next == 0x03)
                {
                    buffer[length++] = 0xFF;
                    i++;
                    continue;
                }
            }
            buffer[length++] = raw[i];
        }
        return length == buffer.Length ? buffer : buffer[..length];
    }

    public static List<KeyValuePair<byte[], byte[]>> ParseRulesPackets(byte[] response)
    {
        var pairs = new List<KeyValuePair<byte[], byte[]>>();
        if (response.Length < 2)
        {
            return pairs;
        }
        var offset = 0;
        if (response.Length >= 5
            && response[0] == 0xFF && response[1] == 0xFF && response[2] == 0xFF && response[3] == 0xFF
            && response[4] == RulesResponseByte)
        {
            offset = 5;
        }
        if (offset + 2 > response.Length)
        {
            return pairs;
        }
        int count = response[offset] | (response[offset + 1] << 8);
        offset += 2;
        for (var i = 0; i < count; i++)
        {
            var key = ReadNullTerminatedBytes(response, ref offset);
            if (key is null)
            {
                break;
            }
            var value = ReadNullTerminatedBytes(response, ref offset);
            if (value is null)
            {
                break;
            }
            pairs.Add(new KeyValuePair<byte[], byte[]>(key, value));
        }
        return pairs;
    }

    public static byte[] AssembleModPayload(List<KeyValuePair<byte[], byte[]>> pairs)
    {
        var pages = new List<KeyValuePair<byte[], byte[]>>();
        foreach (var pair in pairs)
        {
            if (pair.Key.Length == 2 && pair.Key[0] <= pair.Key[1])
            {
                pages.Add(pair);
            }
        }
        if (pages.Count == 0)
        {
            return [];
        }
        pages = pages.OrderBy(p => p.Key[0]).ToList();
        using var stream = new MemoryStream();
        foreach (var page in pages)
        {
            var unescaped = UnescapePayload(page.Value);
            stream.Write(unescaped, 0, unescaped.Length);
        }
        return stream.ToArray();
    }

    public static IReadOnlyList<ModEntry> ParseModList(byte[] assembled)
    {
        var mods = new List<ModEntry>();
        if (assembled.Length < 5)
        {
            return mods;
        }
        var pos = 0;
        var version = assembled[pos++];
        if (version != 2 && version != 3)
        {
            return mods;
        }
        pos++;
        if (pos + 2 > assembled.Length)
        {
            return mods;
        }
        var dlcMask = (ushort)(assembled[pos] | (assembled[pos + 1] << 8));
        pos += 2;
        if (version == 3)
        {
            if (pos >= assembled.Length)
            {
                return mods;
            }
            var difficulty = assembled[pos++];
            if (difficulty != 0)
            {
                pos++;
            }
        }
        pos += BitOperations.PopCount(dlcMask) * 4;
        if (pos < 0 || pos >= assembled.Length)
        {
            return mods;
        }
        int modCount = assembled[pos++];
        for (var i = 0; i < modCount; i++)
        {
            if (pos + 5 > assembled.Length)
            {
                break;
            }
            pos += 4;
            var idLen = assembled[pos++];
            ulong id;
            switch (idLen)
            {
                case 1:
                    if (pos + 1 > assembled.Length)
                    {
                        return mods;
                    }
                    id = assembled[pos];
                    pos += 1;
                    break;
                case 2:
                    if (pos + 2 > assembled.Length)
                    {
                        return mods;
                    }
                    id = BinaryPrimitives.ReadUInt16LittleEndian(assembled.AsSpan(pos));
                    pos += 2;
                    break;
                case 4:
                    if (pos + 4 > assembled.Length)
                    {
                        return mods;
                    }
                    id = BinaryPrimitives.ReadUInt32LittleEndian(assembled.AsSpan(pos));
                    pos += 4;
                    break;
                case 8:
                    if (pos + 8 > assembled.Length)
                    {
                        return mods;
                    }
                    id = BinaryPrimitives.ReadUInt64LittleEndian(assembled.AsSpan(pos));
                    pos += 8;
                    break;
                case 19:
                    if (pos + 4 > assembled.Length)
                    {
                        return mods;
                    }
                    pos += 4;
                    continue;
                default:
                    return mods;
            }
            if (pos >= assembled.Length)
            {
                break;
            }
            int nameLen = assembled[pos++];
            if (pos + nameLen > assembled.Length)
            {
                break;
            }
            var name = nameLen == 0 ? "" : Encoding.UTF8.GetString(assembled, pos, nameLen);
            pos += nameLen;
            mods.Add(new ModEntry
            {
                WorkshopId = id.ToString(CultureInfo.InvariantCulture),
                Name = name
            });
        }
        return mods;
    }

    private static byte[]? ReadNullTerminatedBytes(byte[] data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }
        if (offset >= data.Length)
        {
            return null;
        }
        var result = data[start..offset];
        offset++;
        return result;
    }
}
