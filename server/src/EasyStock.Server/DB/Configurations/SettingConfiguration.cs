using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

// Server-only table: no server_seq, it is never synced.
internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> e)
    {
        e.ToTable("setting");
        e.HasKey(x => x.Key);
        e.Property(x => x.Value).HasColumnType(Shared.Json).IsRequired();
    }
}
