using System.Reflection;

namespace EasyStock.Server.Helpers;

/// <summary>Version constants. Placeholders until the app side defines real values.</summary>
public static class CompatHelper
{
    public const string MinAppVersion = "0.0.0";

    public const int ApiVersion = 1;

    /// <summary>App DB (Room) versions accepted in X-EasyStock-Schema-Version: the one whose shared tables this server matches (docs/schema.dbml).</summary>
    public static readonly IReadOnlyList<int> SupportedSchemaVersions = [10];

    public static string ServerVersion { get; } =
        typeof(CompatHelper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
