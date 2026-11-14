using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class LowStockAlertConfiguration : IEntityTypeConfiguration<LowStockAlert>
{
    public void Configure(EntityTypeBuilder<LowStockAlert> e)
    {
        e.ToTable("low_stock_alert");
        e.Ref<LowStockAlert, Article>(nameof(LowStockAlert.ArticleId), required: true);
        e.HasIndex(x => x.ArticleId);
        // A Guest is hard-deleted on logout: the alert outlives the user who caused it.
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CausedByUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.CausedByUserId);
        e.ServerSeq(); // the migration must add the set_server_seq trigger to low_stock_alert
    }
}
