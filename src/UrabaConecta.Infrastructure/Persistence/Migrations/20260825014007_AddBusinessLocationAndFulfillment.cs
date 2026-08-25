using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessLocationAndFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationMode",
                table: "businesses",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "PublicPhysical");

            migrationBuilder.AddColumn<string>(
                name: "OrderFulfillmentMode",
                table: "businesses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PickupAtPublicLocation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationMode",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "OrderFulfillmentMode",
                table: "businesses");
        }
    }
}
