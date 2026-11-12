using System;
using EasyStock.Server.Helpers;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <inheritdoc />
    public partial class LowStockAlertSyncPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "low_stock_alert",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crossed_at = table.Column<long>(type: "bigint", nullable: false),
                    stock = table.Column<long>(type: "bigint", nullable: false),
                    caused_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_low_stock_alert", x => x.id);
                    table.ForeignKey(
                        name: "fk_low_stock_alert_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "low_stock_state",
                columns: table => new
                {
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_low = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_low_stock_state", x => x.article_id);
                    table.ForeignKey(
                        name: "fk_low_stock_state_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sync_batch",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    response = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_batch", x => x.batch_id);
                    table.ForeignKey(
                        name: "fk_sync_batch_device_session_device_session_id",
                        column: x => x.device_session_id,
                        principalTable: "device_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sync_batch_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_low_stock_alert_article_id",
                table: "low_stock_alert",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_low_stock_alert_server_seq",
                table: "low_stock_alert",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_sync_batch_device_session_id",
                table: "sync_batch",
                column: "device_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_batch_received_at",
                table: "sync_batch",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_sync_batch_user_id",
                table: "sync_batch",
                column: "user_id");

            migrationBuilder.Sql(PushSqlHelper.CreateLowStockAlertTrigger);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PushSqlHelper.DropLowStockAlertTrigger);

            migrationBuilder.DropTable(
                name: "low_stock_alert");

            migrationBuilder.DropTable(
                name: "low_stock_state");

            migrationBuilder.DropTable(
                name: "sync_batch");
        }
    }
}
