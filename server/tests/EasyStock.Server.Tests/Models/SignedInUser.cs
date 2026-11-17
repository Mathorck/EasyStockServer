
namespace EasyStock.Server.Tests.Models;

/// <summary>A signed-in caller of any role (Guests included), with its real user and device session rows.</summary>
public sealed record SignedInUser(Guid UserId, Guid SessionId, Role Role, bool IsGuest, string AccessToken);
