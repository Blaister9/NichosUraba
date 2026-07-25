using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateBusinessConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ParticipatesInAvailability",
                table: "staff_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "staff_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "services",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "services",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "services",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageConfiguration",
                table: "business_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "business_hours",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "availability_exceptions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "availability_exceptions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ClosedAllDay");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "availability_exceptions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                UPDATE availability_exceptions
                SET "Type" = CASE WHEN "IsUnavailable" THEN 'ClosedAllDay' ELSE 'ExtraordinaryOpening' END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_display_order",
                table: "services",
                sql: "\"DisplayOrder\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_availability_exception_range",
                table: "availability_exceptions",
                sql: "\"Type\" = 'ClosedAllDay' OR (\"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_service_display_order",
                table: "services");

            migrationBuilder.DropCheckConstraint(
                name: "ck_availability_exception_range",
                table: "availability_exceptions");

            migrationBuilder.DropColumn(
                name: "ParticipatesInAvailability",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "services");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "services");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "services");

            migrationBuilder.DropColumn(
                name: "CanManageConfiguration",
                table: "business_memberships");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "business_hours");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "availability_exceptions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "availability_exceptions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "availability_exceptions");
        }
    }
}
