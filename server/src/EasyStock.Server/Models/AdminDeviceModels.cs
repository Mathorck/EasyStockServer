namespace EasyStock.Server.Models;

/// <summary>
/// One device session for the Admin device list (<c>GET /api/v1/admin/devices</c>). Only the device name is stored
/// (no platform column). <see cref="RevokedAt"/> and <see cref="RevokeReason"/> are null while the session is live.
/// </summary>
public sealed record AdminDeviceAnswer(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    string DeviceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset? RevokedAt,
    string? RevokeReason);
