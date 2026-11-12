using EasyStock.Server.Helpers;

namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>public_invite</c>, one row (<see cref="SingletonId"/>): the printed QR code for Guests.</summary>
public sealed class PublicInvite
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Null until an Admin generates the first code.</summary>
    public byte[]? TokenHash { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Most live Guest accounts at a time; null means no limit.</summary>
    public int? GuestLimit { get; set; } = AuthLimitsHelper.DefaultGuestLimit;

    public DateTimeOffset CreatedAt { get; set; }
}
