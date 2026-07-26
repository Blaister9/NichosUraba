using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualQueues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanManageQueues",
                table: "business_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "queue_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AverageDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaximumWaiting = table.Column<int>(type: "integer", nullable: false),
                    PublicMessage = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_definitions", x => x.Id);
                    table.UniqueConstraint("AK_queue_definitions_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.CheckConstraint("ck_queue_definition_capacity", "\"MaximumWaiting\" BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_queue_definition_duration", "\"AverageDurationMinutes\" BETWEEN 1 AND 480");
                    table.ForeignKey(
                        name: "FK_queue_definitions_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "queue_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PausedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_sessions", x => x.Id);
                    table.UniqueConstraint("AK_queue_sessions_BusinessId_Id", x => new { x.BusinessId, x.Id });
                    table.ForeignKey(
                        name: "FK_queue_sessions_queue_definitions_BusinessId_QueueDefinition~",
                        columns: x => new { x.BusinessId, x.QueueDefinitionId },
                        principalTable: "queue_definitions",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "queue_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PublicCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeVersion = table.Column<int>(type: "integer", nullable: false),
                    ProtectedAlias = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RestoreCount = table.Column<int>(type: "integer", nullable: false),
                    CallCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ServiceStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_queue_tickets_queue_sessions_BusinessId_QueueSessionId",
                        columns: x => new { x.BusinessId, x.QueueSessionId },
                        principalTable: "queue_sessions",
                        principalColumns: new[] { "BusinessId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_queue_definitions_BusinessId",
                table: "queue_definitions",
                column: "BusinessId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_queue_definitions_BusinessId_Id",
                table: "queue_definitions",
                columns: new[] { "BusinessId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_sessions_BusinessId_Id",
                table: "queue_sessions",
                columns: new[] { "BusinessId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_sessions_BusinessId_QueueDefinitionId",
                table: "queue_sessions",
                columns: new[] { "BusinessId", "QueueDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_queue_sessions_QueueDefinitionId",
                table: "queue_sessions",
                column: "QueueDefinitionId",
                unique: true,
                filter: "\"Status\" IN ('Open', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_queue_tickets_BusinessId_QueueSessionId",
                table: "queue_tickets",
                columns: new[] { "BusinessId", "QueueSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_queue_tickets_BusinessId_Status",
                table: "queue_tickets",
                columns: new[] { "BusinessId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_queue_tickets_PublicCodeHash",
                table: "queue_tickets",
                column: "PublicCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_tickets_QueueSessionId_Number",
                table: "queue_tickets",
                columns: new[] { "QueueSessionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_tickets_QueueSessionId_Status_Number",
                table: "queue_tickets",
                columns: new[] { "QueueSessionId", "Status", "Number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_tickets");

            migrationBuilder.DropTable(
                name: "queue_sessions");

            migrationBuilder.DropTable(
                name: "queue_definitions");

            migrationBuilder.DropColumn(
                name: "CanManageQueues",
                table: "business_memberships");
        }
    }
}
