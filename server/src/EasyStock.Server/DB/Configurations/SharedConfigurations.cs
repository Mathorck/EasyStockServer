using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

// Column names come from the snake_case convention; lengths, uniques and indexes follow schema.dbml.
// Foreign keys are real but never cascade: rows are soft-deleted, and movements are append-only.

internal static class Shared
{
    public const string Json = "jsonb";

    /// <summary>
    /// server_seq column and its index, on every shared table. Set by the database trigger (see DB/ServerSeq.cs):
    /// EF never writes it and reads it back after each insert or update.
    /// </summary>
    public static void ServerSeq<T>(this EntityTypeBuilder<T> e) where T : class
    {
        e.Property<long>("ServerSeq").ValueGeneratedOnAddOrUpdate();
        e.HasIndex("ServerSeq");
    }

    public static ReferenceCollectionBuilder<TPrincipal, T> Ref<T, TPrincipal>(
        this EntityTypeBuilder<T> e, string foreignKey, bool required)
        where T : class where TPrincipal : class =>
        e.HasOne<TPrincipal>().WithMany().HasForeignKey(foreignKey).IsRequired(required).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.ToTable("category");
        e.Property(x => x.Name).HasMaxLength(100);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

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
        e.HasIndex(x => x.Key).IsUnique();
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

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

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> e)
    {
        e.ToTable("article");
        e.Property(x => x.Sku).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(255);
        e.Property(x => x.Tracking).HasMaxLength(10);
        e.Property(x => x.Unit).HasMaxLength(20);
        e.Property(x => x.Barcode).HasMaxLength(64);
        e.Property(x => x.ImageSha256).HasMaxLength(64).IsFixedLength();
        e.Property(x => x.Attributes).HasColumnType(Shared.Json);
        e.Ref<Article, Category>(nameof(Article.CategoryId), required: false);
        e.HasIndex(x => x.Sku).IsUnique();
        e.HasIndex(x => x.Barcode).IsUnique();
        e.HasIndex(x => x.CategoryId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e)
    {
        e.ToTable("location");
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.Icon).HasMaxLength(32);
        e.Property(x => x.Color).HasMaxLength(32);
        e.Property(x => x.Barcode).HasMaxLength(64);
        e.Ref<Location, Location>(nameof(Location.ParentId), required: false);
        e.HasIndex(x => x.Barcode).IsUnique();
        e.HasIndex(x => x.ParentId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> e)
    {
        e.ToTable("item");
        e.Property(x => x.SerialNumber).HasMaxLength(100);
        e.Property(x => x.Status).HasMaxLength(20);
        e.Property(x => x.Attributes).HasColumnType(Shared.Json);
        e.Ref<Item, Article>(nameof(Item.ArticleId), required: true);
        e.Ref<Item, Location>(nameof(Item.LocationId), required: false);
        e.HasIndex(x => x.SerialNumber).IsUnique();
        e.HasIndex(x => x.ArticleId);
        e.HasIndex(x => x.LocationId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> e)
    {
        e.ToTable("stock_movement");
        e.Property(x => x.Reason).HasMaxLength(30);
        e.Ref<StockMovement, Article>(nameof(StockMovement.ArticleId), required: true);
        e.Ref<StockMovement, Item>(nameof(StockMovement.ItemId), required: false);
        e.Ref<StockMovement, Location>(nameof(StockMovement.LocationId), required: true);
        e.HasIndex(x => new { x.ArticleId, x.LocationId });
        e.HasIndex(x => x.ItemId);
        e.HasIndex(x => x.CreatedAt);
        e.ServerSeq();
    }
}

internal sealed class StockReviewConfiguration : IEntityTypeConfiguration<StockReview>
{
    public void Configure(EntityTypeBuilder<StockReview> e)
    {
        e.ToTable("stock_review");
        e.Property(x => x.Status).HasMaxLength(20);
        e.Ref<StockReview, Location>(nameof(StockReview.LocationId), required: true);
        e.HasIndex(x => x.LocationId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

internal sealed class StockReviewLineConfiguration : IEntityTypeConfiguration<StockReviewLine>
{
    public void Configure(EntityTypeBuilder<StockReviewLine> e)
    {
        e.ToTable("stock_review_line");
        e.Property(x => x.Result).HasMaxLength(20);
        e.Property(x => x.PendingItem).HasColumnType(Shared.Json);
        e.Ref<StockReviewLine, StockReview>(nameof(StockReviewLine.ReviewId), required: true);
        e.Ref<StockReviewLine, Article>(nameof(StockReviewLine.ArticleId), required: true);
        e.Ref<StockReviewLine, Item>(nameof(StockReviewLine.ItemId), required: false);
        e.Ref<StockReviewLine, Location>(nameof(StockReviewLine.LocationId), required: true);
        e.Ref<StockReviewLine, Location>(nameof(StockReviewLine.FoundLocationId), required: false);
        e.HasIndex(x => x.ReviewId);
        e.HasIndex(x => x.ArticleId);
        e.HasIndex(x => x.ItemId);
        e.HasIndex(x => x.LocationId);
        e.HasIndex(x => x.FoundLocationId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}
