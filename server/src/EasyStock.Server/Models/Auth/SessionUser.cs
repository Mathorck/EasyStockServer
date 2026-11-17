
namespace EasyStock.Server.Models.Auth;

/// <summary>The user a session belongs to. <c>kind</c> is full or guest, <c>shortCode</c> is XXX-XXX.</summary>
public sealed record SessionUser(Guid Id, string DisplayName, string Role, string Kind, string ShortCode);
