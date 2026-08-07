using System.Net;
using System.Text;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class LogUploadService
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    public const int MaxPerDay = 5;
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(30);

    public static string? CheckRateLimit(LauncherSettings settings, DateTime nowUtc)
    {
        settings.LogUploadTimesUtc ??= new List<DateTime>();
        settings.LogUploadTimesUtc.RemoveAll(t => nowUtc - t > TimeSpan.FromHours(24));
        if (settings.LogUploadTimesUtc.Count >= MaxPerDay)
            return $"Upload limit reached ({MaxPerDay} per day). Try again later.";
        var last = settings.LogUploadTimesUtc.Count > 0 ? settings.LogUploadTimesUtc.Max() : (DateTime?)null;
        if (last.HasValue && nowUtc - last.Value < MinInterval)
        {
            var wait = MinInterval - (nowUtc - last.Value);
            return $"Please wait {Math.Ceiling(wait.TotalMinutes)} more minute(s) before sending again.";
        }
        return null;
    }

    public static void RecordUpload(LauncherSettings settings, DateTime nowUtc)
    {
        settings.LogUploadTimesUtc ??= new List<DateTime>();
        settings.LogUploadTimesUtc.Add(nowUtc);
    }

    public async Task<(bool Ok, string Message)> UploadAsync(string dump, CancellationToken ct = default)
    {
        if (dump.Length > LogDumpService.MaxDumpBytes)
            dump = dump[..LogDumpService.MaxDumpBytes];
        try
        {
            using var client = LauncherConstants.CreateHttpClient(UploadTimeout);
            using var content = new StringContent(dump, new UTF8Encoding(false), "text/plain");
            using var request = new HttpRequestMessage(HttpMethod.Post, LauncherConstants.LogUploadUrl) { Content = content };
            request.Headers.Add("X-Iconic-Client", LauncherConstants.UserAgent);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Log.Information("Logdump uploaded, {Bytes} bytes", dump.Length);
                return (true, "Logs sent to the developer. Thank you!");
            }
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Log.Warning("Logdump upload rejected: {Status} {Body}", (int)response.StatusCode, Truncate(body, 300));
            return response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => (false, "The server is receiving too many logs right now. Try again later."),
                HttpStatusCode.RequestEntityTooLarge => (false, "The log file is too large to send. Use 'Dump logs to Desktop' and send it via Discord."),
                _ => (false, $"Sending failed (server said {(int)response.StatusCode}). Use 'Dump logs to Desktop' and send it via Discord.")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Logdump upload failed");
            return (false, "Sending failed (no connection). Use 'Dump logs to Desktop' and send it via Discord.");
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
