
namespace EasyStock.Server.Models.Admin;

/// <summary>A live device session of a user, as shown in the user list.</summary>
public sealed record AdminUserDevice(Guid Id, string DeviceName, DateTimeOffset LastUsedAt);
