using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> e)
    {
        e.ToTable("attribute_definition");
        e.Property(x => x.Key).HasMaxLength(50);
        e.Property(x => x.Label).HasMaxLength(100);
        e.Property(x => x.DataType).HasMaxLength(20);
        e.Property(x => x.Unit).HasMaxLength(20);
        e.Property(x => x.Options).HasColumnType(Shared.Json);
        e.Property(x => x.FlagWhenPast).HasDefaultValue(false);
        e.Property(x => x.NotifyWhenPast).HasDefaultValue(false);
        e.HasIndex(x => x.Key).IsUnique();
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}
