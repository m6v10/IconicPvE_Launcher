using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class A2SQueryService : IA2SQueryService
{
    private const int TimeoutMs = 2000;
    private static readonly byte[] InfoRequest = BuildInfoRequest();

    public async Task<ServerStatus> QueryAsync(string ip, int queryPort, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var status = await QueryOnceAsync(ip, queryPort, ct).ConfigureAwait(false);
                if (status is not null)
                {
                    return status;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("A2S query attempt {Attempt} failed for {Ip}:{Port}: {Message}", attempt + 1, ip, queryPort, ex.Message);
            }
            if (ct.IsCancellationRequested)
            {
                break;
            }
        }
        return new ServerStatus { Online = false };
    }

    private static async Task<ServerStatus?> QueryOnceAsync(string ip, int port, CancellationToken outerCt)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        timeoutCts.CancelAfter(TimeoutMs);
        var token = timeoutCts.Token;
        using var udp = new UdpClient();
        udp.Connect(ip, port);
        var stopwatch = Stopwatch.StartNew();
        await udp.SendAsync(InfoRequest, token).ConfigureAwait(false);
        var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
        stopwatch.Stop();
        var data = result.Buffer;
        if (data.Length >= 9 && data[4] == 0x41)
        {
            var challenged = new byte[InfoRequest.Length + 4];
            InfoRequest.CopyTo(challenged, 0);
            Array.Copy(data, 5, challenged, InfoRequest.Length, 4);
            stopwatch.Restart();
            await udp.SendAsync(challenged, token).ConfigureAwait(false);
            result = await udp.ReceiveAsync(token).ConfigureAwait(false);
            stopwatch.Stop();
            data = result.Buffer;
        }
        if (data.Length < 7 || data[4] != 0x49)
        {
            return null;
        }
        return ParseInfo(data, (int)stopwatch.ElapsedMilliseconds);
    }

    private static ServerStatus ParseInfo(byte[] data, int pingMs)
    {
        var offset = 6;
        var name = ReadNullTerminated(data, ref offset);
        var map = ReadNullTerminated(data, ref offset);
        ReadNullTerminated(data, ref offset);
        ReadNullTerminated(data, ref offset);
        offset += 2;
        var players = offset < data.Length ? data[offset] : (byte)0;
        offset++;
        var maxPlayers = offset < data.Length ? data[offset] : (byte)0;
        var visibilityOffset = offset + 4;
        var passwordProtected = visibilityOffset < data.Length && data[visibilityOffset] == 1;
        return new ServerStatus
        {
            Online = true,
            Name = name,
            Map = map,
            Players = players,
            MaxPlayers = maxPlayers,
            PingMs = pingMs,
            PasswordProtected = passwordProtected
        };
    }

    private static string ReadNullTerminated(byte[] data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }
        var value = Encoding.UTF8.GetString(data, start, offset - start);
        if (offset < data.Length)
        {
            offset++;
        }
        return value;
    }

    private static byte[] BuildInfoRequest()
    {
        var payload = Encoding.ASCII.GetBytes("Source Engine Query\0");
        var packet = new byte[5 + payload.Length];
        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0xFF;
        packet[3] = 0xFF;
        packet[4] = 0x54;
        payload.CopyTo(packet, 5);
        return packet;
    }
}
