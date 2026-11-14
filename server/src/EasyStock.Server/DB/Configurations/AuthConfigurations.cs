using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
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

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.ToTable("users");
        // Only usernames and passwords: no email, phone or two-factor.
        e.Ignore(x => x.Email);
        e.Ignore(x => x.NormalizedEmail);
        e.Ignore(x => x.EmailConfirmed);
        e.Ignore(x => x.PhoneNumber);
        e.Ignore(x => x.PhoneNumberConfirmed);
        e.Ignore(x => x.TwoFactorEnabled);
        e.HasIndex(x => x.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name").IsUnique();
        e.Property(x => x.UserName).HasMaxLength(32);
        e.Property(x => x.NormalizedUserName).HasMaxLength(32);
        e.Property(x => x.DisplayName).HasMaxLength(50);
        e.Property(x => x.Role).AsWire();
        e.Property(x => x.Kind).HasConversion(AuthColumns.KindToWire).HasMaxLength(10);
        e.Property(x => x.ShortCode).HasMaxLength(6).IsFixedLength();
        e.HasIndex(x => x.ShortCode).IsUnique();
        e.ServerSeq();
    }
}

internal sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> e)
    {
        e.ToTable("device_session");
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.DeviceName).HasMaxLength(100);
        e.Property(x => x.RefreshTokenHash).Sha256();
        e.Property(x => x.PreviousTokenHash).Sha256();
        e.Property(x => x.RevokeReason).HasMaxLength(30);
        e.HasIndex(x => x.RefreshTokenHash).IsUnique();
        e.HasIndex(x => x.PreviousTokenHash).IsUnique();
        e.HasIndex(x => x.UserId);
    }
}

internal sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> e)
    {
        e.ToTable("invite");
        e.Property(x => x.TokenHash).Sha256();
        e.Property(x => x.Role).AsWire();
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.TokenHash).IsUnique();
    }
}

internal sealed class PublicInviteConfiguration : IEntityTypeConfiguration<PublicInvite>
{
    public void Configure(EntityTypeBuilder<PublicInvite> e)
    {
        e.ToTable("public_invite", t => t.HasCheckConstraint("ck_public_invite_singleton", "id = 1"));
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TokenHash).Sha256();
        e.HasIndex(x => x.TokenHash).IsUnique();
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> e)
    {
        e.ToTable("promotion");
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        e.Property(x => x.TargetRole).AsWire();
        e.Property(x => x.EndReason).HasMaxLength(20);
        // At most one pending promotion per user.
        e.HasIndex(x => x.UserId).IsUnique().HasFilter("ended_at IS NULL");
    }
}

internal sealed class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> e)
    {
        e.ToTable("password_reset");
        e.Property(x => x.TokenHash).Sha256();
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.UserId);
    }
}

internal sealed class SetupStateConfiguration : IEntityTypeConfiguration<SetupState>
{
    public void Configure(EntityTypeBuilder<SetupState> e)
    {
        e.ToTable("setup_state", t => t.HasCheckConstraint("ck_setup_state_singleton", "id = 1"));
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TokenHash).Sha256();
    }
}
