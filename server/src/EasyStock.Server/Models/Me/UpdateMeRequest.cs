using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Me;

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
