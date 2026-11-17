namespace EasyStock.Server.Models.Me;

/// <summary>A device session (one per logged-in phone).</summary>
public sealed record PersonalDataDevice(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
