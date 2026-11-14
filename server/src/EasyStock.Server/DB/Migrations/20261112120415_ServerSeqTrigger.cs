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

        // Frozen SQL, written out rather than built from DB/ServerSeq.cs, so a later change there cannot change what
        // this migration did. 76224182963779 is ServerSeq.SyncLockKey.
        private const string CreateFunctionSql =
            """
            CREATE SEQUENCE server_seq AS bigint;
            CREATE FUNCTION set_server_seq() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(76224182963779);
                NEW.server_seq := nextval('server_seq');
                RETURN NEW;
            END
            $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CreateFunctionSql);
            foreach (var table in Tables)
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {table}_server_seq BEFORE INSERT OR UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION set_server_seq();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP FUNCTION set_server_seq() CASCADE; DROP SEQUENCE server_seq;"); // CASCADE drops the triggers
    }
}
