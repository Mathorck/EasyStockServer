using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Admin;

/// <summary><c>POST /admin/users/{id}/promote</c>: the role to give and the short code the Admin read on the Guest's phone.</summary>
public sealed record PromoteUserRequest([Required] string Role, [Required] string ShortCode);
