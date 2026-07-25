using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_business_memberships_AspNetUsers_UserId",
                table: "business_memberships");

            migrationBuilder.AddColumn<bool>(
                name: "CanManageAppointments",
                table: "business_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageMembers",
                table: "business_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "business_memberships",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAtUtc",
                table: "business_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "business_memberships",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "business_memberships",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "business_memberships"
                SET "CanManageAppointments" = TRUE,
                    "CanManageConfiguration" = CASE WHEN "Role" = 'Owner' THEN TRUE ELSE "CanManageConfiguration" END,
                    "CanManageMembers" = CASE WHEN "Role" = 'Owner' THEN TRUE ELSE FALSE END,
                    "CreatedAtUtc" = NOW(),
                    "UpdatedAtUtc" = NOW();
                UPDATE "AspNetUsers"
                SET "DisplayName" = split_part("Email", '@', 1)
                WHERE "DisplayName" = '' AND "Email" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "membership_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreviousState = table.Column<string>(type: "jsonb", nullable: false),
                    NewState = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_audit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_audit_entries_business_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "business_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_audit_entries_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_memberships_BusinessId_IsActive",
                table: "business_memberships",
                columns: new[] { "BusinessId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_business_memberships_BusinessId_Role_IsActive",
                table: "business_memberships",
                columns: new[] { "BusinessId", "Role", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_audit_entries_BusinessId_MembershipId_OccurredAt~",
                table: "membership_audit_entries",
                columns: new[] { "BusinessId", "MembershipId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_audit_entries_BusinessId_OccurredAtUtc",
                table: "membership_audit_entries",
                columns: new[] { "BusinessId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_audit_entries_MembershipId",
                table: "membership_audit_entries",
                column: "MembershipId");

            migrationBuilder.AddForeignKey(
                name: "FK_business_memberships_AspNetUsers_UserId",
                table: "business_memberships",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_business_memberships_AspNetUsers_UserId",
                table: "business_memberships");

            migrationBuilder.DropTable(
                name: "membership_audit_entries");

            migrationBuilder.DropIndex(
                name: "IX_business_memberships_BusinessId_IsActive",
                table: "business_memberships");

            migrationBuilder.DropIndex(
                name: "IX_business_memberships_BusinessId_Role_IsActive",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "CanManageAppointments",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "CanManageMembers",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.AddForeignKey(
                name: "FK_business_memberships_AspNetUsers_UserId",
                table: "business_memberships",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
