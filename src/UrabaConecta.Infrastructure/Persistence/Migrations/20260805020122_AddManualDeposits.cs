using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adelantos manuales por WhatsApp. Es aditiva a propósito: sólo agrega columnas con valor por
    /// defecto y una tabla de auditoría nueva.
    ///
    /// Ninguna fila existente se toca. Los servicios anteriores quedan con RequiresDeposit falso y
    /// tipo None, es decir sin adelanto, que es como venían operando. Las citas anteriores quedan
    /// con DepositStatus NotRequired gracias al valor por defecto de la columna, así que ninguna
    /// cita vieja aparece de pronto debiendo un adelanto. No se modifica ningún precio.
    ///
    /// Reversión: Down borra sólo lo agregado. Se pierde la configuración de adelantos y la
    /// auditoría, pero ninguna cita ni ningún precio anterior.
    /// </summary>
    public partial class AddManualDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepositInstructions",
                table: "services",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DepositType",
                table: "services",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "DepositValue",
                table: "services",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DepositWhatsAppNumber",
                table: "services",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDeposit",
                table: "services",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "appointments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositConfiguredValue",
                table: "appointments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DepositInstructions",
                table: "appointments",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DepositRejectionReason",
                table: "appointments",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepositReportedAtUtc",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositStatus",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "DepositType",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepositVerifiedAtUtc",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepositVerifiedByUserId",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositWhatsAppNumber",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "appointment_deposit_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_deposit_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointment_deposit_audits_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_appointment_deposit_audits_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_BusinessId_DepositStatus",
                table: "appointments",
                columns: new[] { "BusinessId", "DepositStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_deposit_audits_AppointmentId_OccurredAtUtc",
                table: "appointment_deposit_audits",
                columns: new[] { "AppointmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_deposit_audits_BusinessId_OccurredAtUtc",
                table: "appointment_deposit_audits",
                columns: new[] { "BusinessId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_deposit_audits");

            migrationBuilder.DropIndex(
                name: "IX_appointments_BusinessId_DepositStatus",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositInstructions",
                table: "services");

            migrationBuilder.DropColumn(
                name: "DepositType",
                table: "services");

            migrationBuilder.DropColumn(
                name: "DepositValue",
                table: "services");

            migrationBuilder.DropColumn(
                name: "DepositWhatsAppNumber",
                table: "services");

            migrationBuilder.DropColumn(
                name: "RequiresDeposit",
                table: "services");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositConfiguredValue",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositInstructions",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositRejectionReason",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositReportedAtUtc",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositStatus",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositType",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositVerifiedAtUtc",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositVerifiedByUserId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositWhatsAppNumber",
                table: "appointments");
        }
    }
}
