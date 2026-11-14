using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <summary>
    /// sync_batch.request_hash (a reused batch ID with other rows is refused), attribute_alias (attribute merges
    /// kept across batches), at most one open stock_review per location, and the low_stock_alert.caused_by_user_id
    /// foreign key.
    /// </summary>
    public partial class SchemaReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "request_hash",
                table: "sync_batch",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "attribute_alias",
                columns: table => new
                {
                    alias_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_alias", x => x.alias_id);
                    table.ForeignKey(
                        name: "fk_attribute_alias_attribute_definitions_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attribute_definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_stock_review_location_id_open",
                table: "stock_review",
                column: "location_id",
                unique: true,
                filter: "status = 'open' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_low_stock_alert_caused_by_user_id",
                table: "low_stock_alert",
                column: "caused_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_alias_attribute_id",
                table: "attribute_alias",
                column: "attribute_id");

            // Alerts caused by a user deleted before the foreign key existed: the author is unknown.
            migrationBuilder.Sql(
                "UPDATE low_stock_alert a SET caused_by_user_id = NULL WHERE caused_by_user_id IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = a.caused_by_user_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_low_stock_alert_users_caused_by_user_id",
                table: "low_stock_alert",
                column: "caused_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_low_stock_alert_users_caused_by_user_id",
                table: "low_stock_alert");

            migrationBuilder.DropTable(
                name: "attribute_alias");

            migrationBuilder.DropIndex(
                name: "ux_stock_review_location_id_open",
                table: "stock_review");

            migrationBuilder.DropIndex(
                name: "ix_low_stock_alert_caused_by_user_id",
                table: "low_stock_alert");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "sync_batch");
        }
    }
}
