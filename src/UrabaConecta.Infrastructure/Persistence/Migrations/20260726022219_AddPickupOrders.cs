using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupOrderId",
                table: "consent_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageOrders",
                table: "business_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ordering_pickup_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicOrderNumber = table.Column<int>(type: "integer", nullable: false),
                    PickupStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PickupEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProtectedCustomerAlias = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ProtectedCustomerPhone = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PhoneLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    ProtectedNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublicCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ConsentAcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordering_pickup_orders", x => x.Id);
                    table.UniqueConstraint("AK_ordering_pickup_orders_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("ck_pickup_order_range", "\"PickupStartUtc\" < \"PickupEndUtc\"");
                    table.CheckConstraint("ck_pickup_order_totals", "\"Subtotal\" >= 0 AND \"Total\" >= 0");
                    table.ForeignKey(
                        name: "FK_ordering_pickup_orders_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordering_pickup_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PublicMessage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MinimumPreparationMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlotIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaximumActivePerSlot = table.Column<int>(type: "integer", nullable: false),
                    ReceivesFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ReceivesUntil = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    NextOrderNumber = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordering_pickup_settings", x => x.Id);
                    table.CheckConstraint("ck_pickup_settings_capacity", "\"MaximumActivePerSlot\" BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_pickup_settings_range", "\"ReceivesFrom\" < \"ReceivesUntil\"");
                    table.ForeignKey(
                        name: "FK_ordering_pickup_settings_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordering_product_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordering_product_categories", x => x.Id);
                    table.UniqueConstraint("AK_ordering_product_categories_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("ck_product_category_order", "\"DisplayOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_ordering_product_categories_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordering_products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordering_products", x => x.Id);
                    table.UniqueConstraint("AK_ordering_products_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("ck_product_order", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("ck_product_price", "\"ReferencePrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_ordering_products_ordering_product_categories_BusinessId_Pr~",
                        columns: x => new { x.BusinessId, x.ProductCategoryId },
                        principalTable: "ordering_product_categories",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordering_pickup_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ProtectedNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordering_pickup_order_lines", x => x.Id);
                    table.CheckConstraint("ck_pickup_line_prices", "\"UnitPriceSnapshot\" >= 0 AND \"LineTotal\" >= 0");
                    table.CheckConstraint("ck_pickup_line_quantity", "\"Quantity\" BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_ordering_pickup_order_lines_ordering_pickup_orders_Business~",
                        columns: x => new { x.BusinessId, x.PickupOrderId },
                        principalTable: "ordering_pickup_orders",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ordering_pickup_order_lines_ordering_products_BusinessId_Pr~",
                        columns: x => new { x.BusinessId, x.ProductId },
                        principalTable: "ordering_products",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consent_receipts_PickupOrderId",
                table: "consent_receipts",
                column: "PickupOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_order_lines_BusinessId_PickupOrderId",
                table: "ordering_pickup_order_lines",
                columns: new[] { "BusinessId", "PickupOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_order_lines_BusinessId_ProductId",
                table: "ordering_pickup_order_lines",
                columns: new[] { "BusinessId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_orders_BusinessId_Id",
                table: "ordering_pickup_orders",
                columns: new[] { "BusinessId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_orders_BusinessId_PickupStartUtc_Status",
                table: "ordering_pickup_orders",
                columns: new[] { "BusinessId", "PickupStartUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_orders_BusinessId_PublicOrderNumber",
                table: "ordering_pickup_orders",
                columns: new[] { "BusinessId", "PublicOrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_orders_BusinessId_Status_CreatedAtUtc",
                table: "ordering_pickup_orders",
                columns: new[] { "BusinessId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_orders_PublicCodeHash",
                table: "ordering_pickup_orders",
                column: "PublicCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_pickup_settings_BusinessId",
                table: "ordering_pickup_settings",
                column: "BusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_product_categories_BusinessId_Id",
                table: "ordering_product_categories",
                columns: new[] { "BusinessId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_product_categories_BusinessId_IsActive_DisplayOrder",
                table: "ordering_product_categories",
                columns: new[] { "BusinessId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ordering_products_BusinessId_Id",
                table: "ordering_products",
                columns: new[] { "BusinessId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordering_products_BusinessId_ProductCategoryId_IsActive_Dis~",
                table: "ordering_products",
                columns: new[] { "BusinessId", "ProductCategoryId", "IsActive", "DisplayOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_consent_receipts_ordering_pickup_orders_PickupOrderId",
                table: "consent_receipts",
                column: "PickupOrderId",
                principalTable: "ordering_pickup_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consent_receipts_ordering_pickup_orders_PickupOrderId",
                table: "consent_receipts");

            migrationBuilder.DropTable(
                name: "ordering_pickup_order_lines");

            migrationBuilder.DropTable(
                name: "ordering_pickup_settings");

            migrationBuilder.DropTable(
                name: "ordering_pickup_orders");

            migrationBuilder.DropTable(
                name: "ordering_products");

            migrationBuilder.DropTable(
                name: "ordering_product_categories");

            migrationBuilder.DropIndex(
                name: "IX_consent_receipts_PickupOrderId",
                table: "consent_receipts");

            migrationBuilder.DropColumn(
                name: "PickupOrderId",
                table: "consent_receipts");

            migrationBuilder.DropColumn(
                name: "CanManageOrders",
                table: "business_memberships");
        }
    }
}
