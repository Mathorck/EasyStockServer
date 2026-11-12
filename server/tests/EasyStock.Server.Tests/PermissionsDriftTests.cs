using System.Text.RegularExpressions;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// Helpers/PermissionsHelper.cs must give each role exactly the rights of the app's auth/Permissions.kt.
/// The app's EDITOR is the server's Member. Kotlin names are compared as PascalCase (VIEW_STOCK = ViewStock).
/// </summary>
public sealed partial class PermissionsDriftTests
{
    private const string KotlinFile = "shared/src/commonMain/kotlin/ch/mathorck/easystock/auth/Permissions.kt";

    private static readonly Dictionary<string, Role> RoleNames = new()
    {
        ["ADMIN"] = Role.Admin,
        ["EDITOR"] = Role.Member,
        ["VIEWER"] = Role.Viewer,
        ["GUEST"] = Role.Guest,
    };

    [Fact]
    public void Roles_and_rights_match_the_app()
    {
        var kotlin = File.ReadAllText(SchemaDriftTests.FindAppFile(KotlinFile));

        var roles = EnumEntries(kotlin, "Role");
        Assert.Equal(RoleNames.Keys.Order(), roles.Order());

        var actions = EnumEntries(kotlin, "Action").Select(Pascal).ToList();
        Assert.Equal(actions, Enum.GetValues<Permission>().Select(p => p.ToString()));

        var sets = new Dictionary<string, HashSet<string>>();
        foreach (Match m in SetRegex().Matches(kotlin))
            sets[m.Groups[1].Value] = Eval(m.Groups[2].Value, sets, actions);

        var byRole = ByRoleRegex().Match(kotlin);
        Assert.True(byRole.Success, "byRole map not found in Permissions.kt");
        var entries = RoleEntryRegex().Matches(byRole.Groups[1].Value);
        Assert.Equal(roles.Count, entries.Count);
        foreach (Match e in entries)
        {
            var role = RoleNames[e.Groups[1].Value];
            var expected = Eval(e.Groups[2].Value, sets, actions);
            var actual = Permissions.Allowed(role).Select(p => p.ToString()).ToHashSet();
            Assert.True(expected.SetEquals(actual),
                $"{role}: app has [{string.Join(", ", expected.Order())}], server has [{string.Join(", ", actual.Order())}]");
        }

        Assert.Equal(Permissions.Allowed(Role.Guest), Permissions.Allowed(null)); // unknown role = Guest, like the app
    }

    [Fact]
    public void Wire_names_round_trip()
    {
        foreach (var role in Enum.GetValues<Role>()) Assert.Equal(role, Permissions.ParseRole(role.ToWire()));
        Assert.Null(Permissions.ParseRole("editor")); // the wire name is "member"
        Assert.Null(Permissions.ParseRole(null));
    }

    private static List<string> EnumEntries(string kotlin, string name)
    {
        var body = Regex.Match(kotlin, $@"enum class {name}\b[^{{]*\{{(.*?)\n\}}", RegexOptions.Singleline).Groups[1].Value;
        body = Regex.Replace(body, @"/\*.*?\*/", "", RegexOptions.Singleline);
        body = body.Split("companion object")[0];
        return Regex.Matches(body, @"^\s*([A-Z][A-Z_]+)\b", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>Evaluates the set expressions used in Permissions.kt: A + B, setOf(...), Action.entries.toSet().</summary>
    private static HashSet<string> Eval(string expression, Dictionary<string, HashSet<string>> sets, List<string> actions)
    {
        var result = new HashSet<string>();
        foreach (var raw in Regex.Split(expression, @"\+(?![^(]*\))"))
        {
            var term = Regex.Replace(raw, @"\s+", "");
            if (term == "Action.entries.toSet()") result.UnionWith(actions);
            else if (term.StartsWith("setOf(", StringComparison.Ordinal))
                result.UnionWith(term[6..^1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => Pascal(a.Replace("Action.", "", StringComparison.Ordinal))));
            else if (sets.TryGetValue(term, out var set)) result.UnionWith(set);
            else throw new InvalidOperationException($"Permissions.kt: cannot read '{term}', update PermissionsDriftTests");
        }
        return result;
    }

    private static string Pascal(string upper) =>
        string.Concat(upper.Split('_').Select(w => w[0] + w[1..].ToLowerInvariant()));

    [GeneratedRegex(@"private val (\w+) = (setOf\(.*?\))\s*\n", RegexOptions.Singleline)]
    private static partial Regex SetRegex();

    [GeneratedRegex(@"byRole[^=]*= mapOf\((.*?)\n\s*\)", RegexOptions.Singleline)]
    private static partial Regex ByRoleRegex();

    [GeneratedRegex(@"Role\.(\w+) to (.+?),?\s*$", RegexOptions.Multiline)]
    private static partial Regex RoleEntryRegex();
}
