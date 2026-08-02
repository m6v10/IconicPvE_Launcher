using System.Text.Json;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed record PublishedFileDetails(string PublishedFileId, int Result, string? Title, string? HContentFile, long TimeUpdated, long FileSize);

public sealed class WorkshopWebApi
{
    private const string EndpointUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private const int ChunkSize = 100;
    private const int MaxAttempts = 3;
    private readonly HttpClient _http;

    public WorkshopWebApi(HttpClient? http = null)
    {
        _http = http ?? LauncherConstants.CreateHttpClient(TimeSpan.FromSeconds(30));
    }

    public async Task<Dictionary<string, PublishedFileDetails>> GetDetailsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var all = ids.Distinct().ToList();
        var result = new Dictionary<string, PublishedFileDetails>();
        for (var offset = 0; offset < all.Count; offset += ChunkSize)
        {
            var chunk = all.Skip(offset).Take(ChunkSize).ToList();
            var details = await FetchChunkAsync(chunk, ct).ConfigureAwait(false);
            foreach (var d in details)
                result[d.PublishedFileId] = d;
        }
        return result;
    }

    private async Task<List<PublishedFileDetails>> FetchChunkAsync(List<string> chunk, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var form = new List<KeyValuePair<string, string>> { new("itemcount", chunk.Count.ToString()) };
                for (var i = 0; i < chunk.Count; i++)
                    form.Add(new($"publishedfileids[{i}]", chunk[i]));
                using var content = new FormUrlEncodedContent(form);
                using var response = await _http.PostAsync(EndpointUrl, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseResponse(json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warning(ex, "GetPublishedFileDetails attempt {Attempt} of {Max} failed for {Count} ids", attempt, MaxAttempts, chunk.Count);
                if (attempt < MaxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct).ConfigureAwait(false);
            }
        }
        throw new HttpRequestException($"GetPublishedFileDetails failed after {MaxAttempts} attempts", last);
    }

    private static List<PublishedFileDetails> ParseResponse(string json)
    {
        var list = new List<PublishedFileDetails>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response))
            return list;
        if (!response.TryGetProperty("publishedfiledetails", out var details) || details.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in details.EnumerateArray())
        {
            var id = ReadString(item, "publishedfileid");
            if (string.IsNullOrEmpty(id))
                continue;
            list.Add(new PublishedFileDetails(
                id,
                (int)ReadLong(item, "result"),
                ReadString(item, "title"),
                ReadString(item, "hcontent_file"),
                ReadLong(item, "time_updated"),
                ReadLong(item, "file_size")));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }

    private static long ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return 0;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var number))
            return number;
        if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var parsed))
            return parsed;
        return 0;
    }
}
