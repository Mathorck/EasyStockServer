using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

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
