namespace EasyStock.Server.Models.Me;

/// <summary>The session of a device; expiresAt is null once revoked.</summary>
public sealed record PersonalDataSession(
    Guid DeviceId, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt, string? RevokeReason);
