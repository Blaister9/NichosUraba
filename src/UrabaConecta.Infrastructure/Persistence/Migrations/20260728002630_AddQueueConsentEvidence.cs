using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueConsentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "consent_receipts",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QueueTicketId",
                table: "consent_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consent_receipts_QueueTicketId",
                table: "consent_receipts",
                column: "QueueTicketId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_consent_receipts_queue_tickets_QueueTicketId",
                table: "consent_receipts",
                column: "QueueTicketId",
                principalTable: "queue_tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consent_receipts_queue_tickets_QueueTicketId",
                table: "consent_receipts");

            migrationBuilder.DropIndex(
                name: "IX_consent_receipts_QueueTicketId",
                table: "consent_receipts");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "consent_receipts");

            migrationBuilder.DropColumn(
                name: "QueueTicketId",
                table: "consent_receipts");
        }
    }
}
