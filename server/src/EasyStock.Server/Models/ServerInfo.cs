namespace EasyStock.Server.Models;

public sealed record ServerInfo(
    string ServerName,
    string ServerVersion,
    int ApiVersion,
    string MinAppVersion,
    IReadOnlyList<int> SupportedSchemaVersions,
    bool SetupDone,
    bool HasData,
    bool GuestSignupEnabled,
    string DatasetEpoch);
