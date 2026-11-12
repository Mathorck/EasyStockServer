using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <summary>Global server_seq sequence and the trigger that sets it on the shared tables (DB/ServerSeq.cs).</summary>
    public partial class ServerSeqTrigger : Migration
    {
        // Frozen list: a later migration adds the trigger to new pullable tables (low_stock_alert, users).
        private static readonly string[] Tables =
        [
            "category", "attribute_definition", "category_attribute", "article", "location",
            "item", "stock_movement", "stock_review", "stock_review_line",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ServerSeq.CreateFunctionSql);
            foreach (var table in Tables) migrationBuilder.Sql(ServerSeq.CreateTriggerSql(table));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(ServerSeq.DropFunctionSql); // CASCADE drops the triggers
    }
}
