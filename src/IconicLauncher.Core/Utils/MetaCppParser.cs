using System.Text.RegularExpressions;

namespace IconicLauncher.Core.Utils;

public sealed record MetaCppInfo(string PublishedId, string Name);

public static class MetaCppParser
{
    private static readonly Regex IdRegex = new(@"publishedid\s*=\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NameRegex = new("name\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MetaCppInfo? Parse(string content)
    {
        var idMatch = IdRegex.Match(content);
        if (!idMatch.Success)
            return null;
        if (!ulong.TryParse(idMatch.Groups[1].Value, out var id) || id == 0)
            return null;
        var nameMatch = NameRegex.Match(content);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value : "";
        return new MetaCppInfo(idMatch.Groups[1].Value, name);
    }
}
