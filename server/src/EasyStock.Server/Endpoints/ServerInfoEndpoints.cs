using EasyStock.Server.Infrastructure;

namespace EasyStock.Server.Endpoints;

public static class ServerInfoEndpoints
{
    public static void MapServerInfo(this RouteGroupBuilder api) =>
        api.MapGet("/server-info", () => new ServerInfo(
            ServerName: "EasyStock",
            ServerVersion: Compat.ServerVersion,
            ApiVersion: Compat.ApiVersion,
            MinAppVersion: Compat.MinAppVersion,
            SupportedSchemaVersions: Compat.SupportedSchemaVersions,
            SetupDone: false,
            HasData: false,
            GuestSignupEnabled: false));
}

// Placeholder values until setup state and settings exist.
public sealed record ServerInfo(
    string ServerName,
    string ServerVersion,
    int ApiVersion,
    string MinAppVersion,
    IReadOnlyList<int> SupportedSchemaVersions,
    bool SetupDone,
    bool HasData,
    bool GuestSignupEnabled);
