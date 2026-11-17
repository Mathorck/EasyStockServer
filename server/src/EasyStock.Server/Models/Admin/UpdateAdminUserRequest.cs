
namespace EasyStock.Server.Models.Admin;

/// <summary>
/// <c>PATCH /admin/users/{id}</c>: only the fields present change. <c>disabled: true</c> disables the user and
/// ends their sessions, <c>false</c> enables them again.
/// </summary>
public sealed class UpdateAdminUserRequest
{
    public string? Role { get; set; }
    public string? DisplayName { get; set; }
    public bool? Disabled { get; set; }
}
