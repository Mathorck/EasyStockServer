using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class ArticleFlagConfiguration : IEntityTypeConfiguration<ArticleFlag>
{
    public void Configure(EntityTypeBuilder<ArticleFlag> e)
    {
        e.ToTable("article_flag");
        e.Property(x => x.Note).HasColumnType("text");
        e.Ref<ArticleFlag, Article>(nameof(ArticleFlag.ArticleId), required: true);
        e.HasIndex(x => x.ArticleId);
        e.HasIndex(x => x.UpdatedAt);
        // Pull names only users some movement or flag names (PullUserDirectoryHandler).
        e.HasIndex(x => x.CreatedBy);
        e.ServerSeq();
    }
}
