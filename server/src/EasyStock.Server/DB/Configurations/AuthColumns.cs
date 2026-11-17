using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

// Server-only tables (docs/04-server-implementation-plan.md, Phase 2): never synced, timestamps are timestamptz.
// Tokens are only ever stored as their SHA-256 (bytea).

public static class AuthColumns
{
    public static readonly ValueConverter<Role, string> RoleToWire =
        new(r => r.ToWire(), s => Permissions.ParseRole(s) ?? Role.Guest);

    public static readonly ValueConverter<UserKind, string> KindToWire =
        new(k => k == UserKind.Guest ? "guest" : "full", s => KindFromWire(s));

    /// <summary>Fail closed: an unknown stored value is an error, never a Full account.</summary>
    public static UserKind KindFromWire(string s) => s switch
    {
        "full" => UserKind.Full,
        "guest" => UserKind.Guest,
        _ => throw new InvalidOperationException($"Unknown users.kind value '{s}'."),
    };

    public static PropertyBuilder<Role> AsWire(this PropertyBuilder<Role> p) => p.HasConversion(RoleToWire).HasMaxLength(10);

    public static PropertyBuilder<T> Sha256<T>(this PropertyBuilder<T> p) => p.HasColumnType("bytea");
}
