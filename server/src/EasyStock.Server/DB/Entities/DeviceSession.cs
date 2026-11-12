namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>device_session</c>: one per logged-in phone. Refresh tokens are stored only as SHA-256.</summary>
public sealed class DeviceSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string DeviceName { get; set; } = "";
    public byte[] RefreshTokenHash { get; set; } = [];

    /// <summary>Hash of the token before the last rotation: presenting it again revokes the session.</summary>
    public byte[]? PreviousTokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>logout, token-reuse, expired, password-reset, password-changed, admin, disabled...</summary>
    public string? RevokeReason { get; set; }
}
