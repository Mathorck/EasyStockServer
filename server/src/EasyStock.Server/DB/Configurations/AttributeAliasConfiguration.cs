using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class AttributeAliasConfiguration : IEntityTypeConfiguration<AttributeAlias>
{
    public void Configure(EntityTypeBuilder<AttributeAlias> e)
    {
        e.ToTable("attribute_alias");
        e.HasKey(x => x.AliasId);
        e.Property(x => x.AliasId).ValueGeneratedNever();
        e.Ref<AttributeAlias, AttributeDefinition>(nameof(AttributeAlias.AttributeId), required: true);
        e.HasIndex(x => x.AttributeId);
    }
}
