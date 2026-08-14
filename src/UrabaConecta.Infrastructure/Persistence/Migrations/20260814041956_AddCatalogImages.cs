using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "business_images",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceId",
                table: "business_images",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_images_ProductId",
                table: "business_images",
                column: "ProductId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_business_images_ServiceId",
                table: "business_images",
                column: "ServiceId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"ServiceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_business_images_ordering_products_ProductId",
                table: "business_images",
                column: "ProductId",
                principalTable: "ordering_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_business_images_services_ServiceId",
                table: "business_images",
                column: "ServiceId",
                principalTable: "services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_business_images_ordering_products_ProductId",
                table: "business_images");

            migrationBuilder.DropForeignKey(
                name: "FK_business_images_services_ServiceId",
                table: "business_images");

            migrationBuilder.DropIndex(
                name: "IX_business_images_ProductId",
                table: "business_images");

            migrationBuilder.DropIndex(
                name: "IX_business_images_ServiceId",
                table: "business_images");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "business_images");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "business_images");
        }
    }
}
