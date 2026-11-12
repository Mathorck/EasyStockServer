using System.Collections.Frozen;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

public static class Permissions
{
    private static readonly Permission[] Read =
        [Permission.ViewStock, Permission.ViewHistory, Permission.ViewReviews, Permission.ReceiveAlerts];

    private static readonly Permission[] Write =
    [
        Permission.StockAction,
        Permission.Edit,
        Permission.Delete,
        Permission.LinkTag,
        Permission.RunReview,
        Permission.ManageSettings,
        Permission.PushChanges,
    ];

    private static readonly FrozenDictionary<Role, FrozenSet<Permission>> ByRole = new Dictionary<Role, FrozenSet<Permission>>
    {
        [Role.Admin] = Enum.GetValues<Permission>().ToFrozenSet(),
        [Role.Member] = Read.Concat(Write).ToFrozenSet(),
        [Role.Viewer] = Read.ToFrozenSet(),
        [Role.Guest] = new[] { Permission.ViewStock }.ToFrozenSet(),
    }.ToFrozenDictionary();

    /// <summary>What <paramref name="role"/> can do. An unknown role (null) gets the rights of a Guest.</summary>
    public static IReadOnlySet<Permission> Allowed(Role? role) => ByRole[role ?? Role.Guest];

    public static bool Can(Role? role, Permission permission) => Allowed(role).Contains(permission);

    /// <summary>The value stored in the database and sent in JSON and in the token.</summary>
    public static string ToWire(this Role role) => role switch
    {
        Role.Admin => "admin",
        Role.Member => "member",
        Role.Viewer => "viewer",
        Role.Guest => "guest",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>Null for an unknown value, which <see cref="Allowed"/> treats as a Guest.</summary>
    public static Role? ParseRole(string? wire) => wire switch
    {
        "admin" => Role.Admin,
        "member" => Role.Member,
        "viewer" => Role.Viewer,
        "guest" => Role.Guest,
        _ => null,
    };
}
