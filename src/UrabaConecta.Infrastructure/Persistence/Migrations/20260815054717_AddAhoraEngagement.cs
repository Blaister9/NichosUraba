using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAhoraEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "ordering_products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "business_promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Headline = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: false),
                    Body = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    CtaLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeepLink = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PushSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_promotions_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_promotions_BusinessId_IsActive_StartsAtUtc_EndsAtU~",
                table: "business_promotions",
                columns: new[] { "BusinessId", "IsActive", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_business_promotions_BusinessId_PushSentAtUtc",
                table: "business_promotions",
                columns: new[] { "BusinessId", "PushSentAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_promotions");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "ordering_products");
        }
    }
}
