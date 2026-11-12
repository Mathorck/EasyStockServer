using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Server.DB.Migrations
{
    /// <inheritdoc />
    public partial class InitialSharedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attribute_definition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_definition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    name_template = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "location",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    icon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location", x => x.id);
                    table.ForeignKey(
                        name: "fk_location_location_parent_id",
                        column: x => x.parent_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "article",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_auto = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    threshold = table.Column<long>(type: "bigint", nullable: true),
                    image_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category_attribute",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_attribute", x => x.id);
                    table.ForeignKey(
                        name: "fk_category_attribute_attribute_definition_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attribute_definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_category_attribute_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    finished_at = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_review", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_review_location_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movement", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movement_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movement_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movement_location_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_review_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_quantity = table.Column<long>(type: "bigint", nullable: false),
                    counted_quantity = table.Column<long>(type: "bigint", nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    found_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    info_flagged = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    info_fixed_at = table.Column<long>(type: "bigint", nullable: true),
                    pending_item = table.Column<string>(type: "jsonb", nullable: true),
                    checked_at = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_review_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_review_line_article_article_id",
                        column: x => x.article_id,
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_review_line_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_review_line_location_found_location_id",
                        column: x => x.found_location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_review_line_location_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_review_line_stock_review_review_id",
                        column: x => x.review_id,
                        principalTable: "stock_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_barcode",
                table: "article",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_category_id",
                table: "article",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_server_seq",
                table: "article",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_article_sku",
                table: "article",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_updated_at",
                table: "article",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_definition_key",
                table: "attribute_definition",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_definition_server_seq",
                table: "attribute_definition",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_definition_updated_at",
                table: "attribute_definition",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_category_name",
                table: "category",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_server_seq",
                table: "category",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_category_updated_at",
                table: "category",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_attribute_id",
                table: "category_attribute",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_category_id_attribute_id",
                table: "category_attribute",
                columns: new[] { "category_id", "attribute_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_server_seq",
                table: "category_attribute",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_updated_at",
                table: "category_attribute",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_item_article_id",
                table: "item",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_location_id",
                table: "item",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_serial_number",
                table: "item",
                column: "serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_server_seq",
                table: "item",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_item_updated_at",
                table: "item",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_location_barcode",
                table: "location",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_parent_id",
                table: "location",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_server_seq",
                table: "location",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_location_updated_at",
                table: "location",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_article_id_location_id",
                table: "stock_movement",
                columns: new[] { "article_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_created_at",
                table: "stock_movement",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_item_id",
                table: "stock_movement",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_location_id",
                table: "stock_movement",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_server_seq",
                table: "stock_movement",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_location_id",
                table: "stock_review",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_server_seq",
                table: "stock_review",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_updated_at",
                table: "stock_review",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_article_id",
                table: "stock_review_line",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_found_location_id",
                table: "stock_review_line",
                column: "found_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_item_id",
                table: "stock_review_line",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_location_id",
                table: "stock_review_line",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_review_id",
                table: "stock_review_line",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_server_seq",
                table: "stock_review_line",
                column: "server_seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_review_line_updated_at",
                table: "stock_review_line",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_attribute");

            migrationBuilder.DropTable(
                name: "stock_movement");

            migrationBuilder.DropTable(
                name: "stock_review_line");

            migrationBuilder.DropTable(
                name: "attribute_definition");

            migrationBuilder.DropTable(
                name: "item");

            migrationBuilder.DropTable(
                name: "stock_review");

            migrationBuilder.DropTable(
                name: "article");

            migrationBuilder.DropTable(
                name: "location");

            migrationBuilder.DropTable(
                name: "category");
        }
    }
}
