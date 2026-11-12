using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models;

/// <summary>Body of <c>POST /api/v1/admin/invites</c>. The role is <c>admin</c>, <c>member</c> or <c>viewer</c>.</summary>
public sealed record CreateInviteRequest([Required] string? Role)
{
    public override string ToString() => $"CreateInviteRequest(role={Role})";
}
