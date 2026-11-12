using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

/// <summary>The Admin settings, sent and returned as a whole. Unknown JSON properties are rejected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ServerSettings(string ServerName, int? MovementAuthorRetentionDays)
{
    public const string DefaultServerName = "EasyStock";
    public const int MaxServerNameLength = 50;

    public static ServerSettings Defaults { get; } = new(DefaultServerName, null);

    /// <summary>Returns a message for the first invalid value, or null when every value is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
            return "serverName must not be empty.";
        if (ServerName.Length > MaxServerNameLength)
            return $"serverName must be at most {MaxServerNameLength} characters.";
        if (MovementAuthorRetentionDays is < 1)
            return "movementAuthorRetentionDays must be at least 1, or null to keep all author names.";
        return null;
    }
}
