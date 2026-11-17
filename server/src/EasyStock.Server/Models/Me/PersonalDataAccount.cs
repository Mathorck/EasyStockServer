namespace EasyStock.Server.Models.Me;

/// <summary>Username is null for a Guest; alertsEnabled null means the role default.</summary>
public sealed record PersonalDataAccount(
    Guid Id, string? Username, string DisplayName, string Role, string Kind, string ShortCode,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, bool? AlertsEnabled, bool Disabled);
