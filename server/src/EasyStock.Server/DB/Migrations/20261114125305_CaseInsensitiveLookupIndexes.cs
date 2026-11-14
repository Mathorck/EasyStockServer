using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <summary>
    /// Indexes for lookups made under the sync lock: <c>lower(...)</c> expression indexes for the case-insensitive
    /// uniqueness checks of push (category name, article SKU, item serial number), which otherwise scan the whole
    /// table for every row and every suffix tried, and <c>stock_movement.created_by</c> for the pull's movement-author
    /// filter on users. Not unique: names are only unique ignoring case by the push rules, and existing rows may
    /// differ by case only. EF Core has no expression index API, so those are raw SQL outside the model.
    /// </summary>
    public partial class CaseInsensitiveLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_created_by",
                table: "stock_movement",
                column: "created_by");

            migrationBuilder.Sql("CREATE INDEX ix_category_name_lower ON category (lower(name));");
            migrationBuilder.Sql("CREATE INDEX ix_article_sku_lower ON article (lower(sku));");
            migrationBuilder.Sql("CREATE INDEX ix_item_serial_number_lower ON item (lower(serial_number));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_item_serial_number_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_article_sku_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_category_name_lower;");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_created_by",
                table: "stock_movement");
        }
    }
}
