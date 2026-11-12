using System.Reflection;

namespace EasyStock.Server.Helpers;

/// <summary>Version constants. Placeholders until the app side defines real values.</summary>
public static class CompatHelper
{
    public const string MinAppVersion = "0.0.0";

    public const int ApiVersion = 1;

    /// <summary>App DB (Room) versions accepted in X-EasyStock-Schema-Version: those whose shared tables this server matches (docs/schema.dbml). 11 only adds an app-only table, so 10 and 11 share the same synced tables.</summary>
    public static readonly IReadOnlyList<int> SupportedSchemaVersions = [10, 11];

    public static string ServerVersion { get; } =
        typeof(CompatHelper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
