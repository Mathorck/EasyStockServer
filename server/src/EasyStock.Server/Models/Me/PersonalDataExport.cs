namespace EasyStock.Server.Models.Me;

// The shape of GET /me/data, for the OpenAPI description only: PersonalDataHandler writes the same names with a
// Utf8JsonWriter, so the movements can be streamed.

/// <summary><c>GET /me/data</c>: the caller's personal data (GDPR art. 15 and 20).</summary>
public sealed record PersonalDataExport(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    PersonalDataServer Server,
    PersonalDataAccount Account,
    IReadOnlyList<PersonalDataDevice> Devices,
    IReadOnlyList<PersonalDataSession> Sessions,
    IReadOnlyList<PersonalDataPushRegistration> PushRegistrations,
    IReadOnlyList<PersonalDataLowStockAlert> LowStockAlerts,
    IReadOnlyList<PersonalDataMovement> Movements);
