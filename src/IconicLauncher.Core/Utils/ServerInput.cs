using System.Globalization;
using System.Net;
using System.Text;

namespace IconicLauncher.Core.Utils;

/// <summary>
/// Parsing and validation for the player-entered server address in the Add Server dialog.
/// </summary>
public static class ServerInput
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;
    public const string CustomIdPrefix = "custom-";

    /// <summary>
    /// Accepts "1.2.3.4", "1.2.3.4:2302" and "1.2.3.4:2302:2303" so a pasted address just works.
    /// </summary>
    public static bool TrySplitAddress(string? input, out string host, out int? gamePort, out int? queryPort)
    {
        host = "";
        gamePort = null;
        queryPort = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var text = input.Trim();
        if (text.StartsWith("steam://connect/", StringComparison.OrdinalIgnoreCase))
        {
            text = text["steam://connect/".Length..].TrimEnd('/');
        }
        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }
        host = parts[0];
        if (parts.Length > 1 && TryParsePort(parts[1], out var parsedGame))
        {
            gamePort = parsedGame;
        }
        if (parts.Length > 2 && TryParsePort(parts[2], out var parsedQuery))
        {
            queryPort = parsedQuery;
        }
        return IsValidHost(host);
    }

    public static bool TryParsePort(string? text, out int port)
    {
        port = 0;
        if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }
        if (value < MinPort || value > MaxPort)
        {
            return false;
        }
        port = value;
        return true;
    }

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }
        var text = host.Trim();
        if (IPAddress.TryParse(text, out _))
        {
            return true;
        }
        if (text.Length > 253 || text.StartsWith('.') || text.EndsWith('.') || text.Contains(".."))
        {
            return false;
        }
        if (!text.Contains('.'))
        {
            return false;
        }
        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Deterministic so that re-adding the same address collides with the existing entry
    /// instead of creating a duplicate card.
    /// </summary>
    public static string BuildCustomId(string host, int gamePort)
    {
        var builder = new StringBuilder(CustomIdPrefix);
        foreach (var c in (host ?? "").Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }
        builder.Append('-');
        builder.Append(gamePort.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    public static bool IsCustomId(string? id)
    {
        return id is { Length: > 0 } && id.StartsWith(CustomIdPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
