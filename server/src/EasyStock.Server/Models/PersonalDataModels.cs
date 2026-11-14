namespace EasyStock.Server.Models;

// The shape of GET /me/data, for the OpenAPI description only: PersonalDataHandler writes the same names with a
// Utf8JsonWriter, so the movements can be streamed.

/// <summary><c>GET /me/data</c>: the caller's personal data (GDPR art. 15 and 20).</summary>
public sealed record PersonalDataExport(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    PersonalDataServer Server,
    PersonalDataAccount Account,
    IReadOnlyList<PersonalDataDevice> Devices,
    IReadOnlyList<PersonalDataSession> Sessions,
    IReadOnlyList<PersonalDataPushRegistration> PushRegistrations,
    IReadOnlyList<PersonalDataLowStockAlert> LowStockAlerts,
    IReadOnlyList<PersonalDataMovement> Movements);

public sealed record PersonalDataServer(string PublicUrl);

/// <summary>Username is null for a Guest; alertsEnabled null means the role default.</summary>
public sealed record PersonalDataAccount(
    Guid Id, string? Username, string DisplayName, string Role, string Kind, string ShortCode,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, bool? AlertsEnabled, bool Disabled);

/// <summary>A device session (one per logged-in phone).</summary>
public sealed record PersonalDataDevice(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

/// <summary>The session of a device; expiresAt is null once revoked.</summary>
public sealed record PersonalDataSession(
    Guid DeviceId, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt, string? RevokeReason);

/// <summary>Only the host of the UnifiedPush endpoint: the full URL holds a secret.</summary>
public sealed record PersonalDataPushRegistration(Guid DeviceId, string? EndpointHost, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A low-stock alert a movement of the user caused. Stock in thousandths, at the moment it went below the threshold.</summary>
public sealed record PersonalDataLowStockAlert(Guid Id, Guid ArticleId, string? ArticleName, DateTimeOffset CrossedAt, long Stock);

/// <summary>A movement the user made. Quantity in thousandths, as stored; reason as stored (consumption...).</summary>
public sealed record PersonalDataMovement(
    Guid Id, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, Guid ArticleId, string? ArticleName, Guid? ItemId,
    Guid LocationId, string? LocationName, long Quantity, string Reason, string? Comment, Guid? DeviceId);
