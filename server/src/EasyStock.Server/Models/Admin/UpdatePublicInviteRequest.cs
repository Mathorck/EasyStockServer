using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Admin;

/// <summary>
/// <c>PUT /admin/public-invite</c>: the whole state of the printed Guest QR code. A missing <c>guestLimit</c>
/// means no limit.
/// </summary>
public sealed record UpdatePublicInviteRequest([Required] bool? Enabled, int? GuestLimit);
