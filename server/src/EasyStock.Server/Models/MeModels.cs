using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

/// <summary><c>GET /me</c>.</summary>
public sealed record MeAnswer(
    Guid Id,
    string DisplayName,
    string Role,
    string Kind,
    string ShortCode,
    bool? AlertsEnabled,
    PendingPromotionAnswer? PendingPromotion);

/// <summary>A pending promotion: the role a Guest gets once they choose a username and password.</summary>
public sealed record PendingPromotionAnswer(string Role, DateTimeOffset ExpiresAt);

/// <summary>
/// <c>PATCH /me</c>: only the fields present in the JSON change. <c>alertsEnabled: null</c> goes back to the
/// role default, a missing <c>alertsEnabled</c> leaves it as it is.
/// </summary>
public sealed class UpdateMeRequest
{
    public string? DisplayName { get; set; }

    public bool? AlertsEnabled
    {
        get;
        set
        {
            field = value;
            HasAlertsEnabled = true;
        }
    }

    [JsonIgnore]
    public bool HasAlertsEnabled { get; private set; }
}

/// <summary><c>POST /me/password</c>.</summary>
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword)
{
    public override string ToString() => "ChangePasswordRequest";
}
