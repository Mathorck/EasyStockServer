using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <summary>
    /// App data schema 13: <c>article.order_url</c>, the <c>attribute_definition</c> flag columns and the synced
    /// <c>article_flag</c> table, with its server_seq trigger (raw SQL, like the other shared tables).
    /// </summary>
    public partial class SchemaVersion13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "order_url",
                table: "article",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "flag_when_past",
                table: "attribute_definition",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "notify_when_past",
                table: "attribute_definition",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "article_flag",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_flag", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_flag_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_flag_article_id",
                table: "article_flag",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_flag_created_by",
                table: "article_flag",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_article_flag_server_seq",
                table: "article_flag",
                column: "server_seq");

            // Redundant now: the (location_id, article_id, quantity) index starts with location_id.
            migrationBuilder.DropIndex(
                name: "ix_stock_movement_location_id",
                table: "stock_movement");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_article_id_location_id_quantity",
                table: "stock_movement",
                columns: new[] { "article_id", "location_id", "quantity" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_article_id_occurred_at_created_at_id",
                table: "stock_movement",
                columns: new[] { "article_id", "occurred_at", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_location_id_article_id_quantity",
                table: "stock_movement",
                columns: new[] { "location_id", "article_id", "quantity" });

            migrationBuilder.CreateIndex(
                name: "ix_article_flag_updated_at",
                table: "article_flag",
                column: "updated_at");

            // set_server_seq() comes from the ServerSeqTrigger migration. Frozen SQL, like the other tables.
            migrationBuilder.Sql(
                "CREATE TRIGGER article_flag_server_seq BEFORE INSERT OR UPDATE ON article_flag FOR EACH ROW EXECUTE FUNCTION set_server_seq();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_location_id",
                table: "stock_movement",
                column: "location_id");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_article_id_location_id_quantity",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_article_id_occurred_at_created_at_id",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_location_id_article_id_quantity",
                table: "stock_movement");

            migrationBuilder.DropTable(
                name: "article_flag");

            migrationBuilder.DropColumn(
                name: "notify_when_past",
                table: "attribute_definition");

            migrationBuilder.DropColumn(
                name: "flag_when_past",
                table: "attribute_definition");

            migrationBuilder.DropColumn(
                name: "order_url",
                table: "article");
        }
    }
}
