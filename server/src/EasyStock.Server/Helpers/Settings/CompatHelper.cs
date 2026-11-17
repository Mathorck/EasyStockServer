using System.Reflection;

namespace EasyStock.Server.Helpers.Settings;

/// <summary>Version constants. Placeholders until the app side defines real values.</summary>
public static class CompatHelper
{
    public const string MinAppVersion = "0.0.0";

    public const int ApiVersion = 1;

    /// <summary>App DB (Room) versions accepted in X-EasyStock-Schema-Version: only the version whose shared tables this server matches (docs/schema.dbml). 13 adds article.order_url, the attribute_definition flag columns and the article_flag table.</summary>
    public static readonly IReadOnlyList<int> SupportedSchemaVersions = [13];

    public static string ServerVersion { get; } =
        typeof(CompatHelper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
