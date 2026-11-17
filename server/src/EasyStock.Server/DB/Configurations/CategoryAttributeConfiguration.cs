using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class CategoryAttributeConfiguration : IEntityTypeConfiguration<CategoryAttribute>
{
    public void Configure(EntityTypeBuilder<CategoryAttribute> e)
    {
        e.ToTable("category_attribute");
        e.Ref<CategoryAttribute, Category>(nameof(CategoryAttribute.CategoryId), required: true);
        e.Ref<CategoryAttribute, AttributeDefinition>(nameof(CategoryAttribute.AttributeId), required: true);
        e.HasIndex(x => new { x.CategoryId, x.AttributeId }).IsUnique();
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}
