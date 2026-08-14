using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_push_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Audience = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Auth = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedDeepLink = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_push_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_web_push_subscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_web_push_subscriptions_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_push_subscriptions_BusinessId_Audience_IsActive",
                table: "web_push_subscriptions",
                columns: new[] { "BusinessId", "Audience", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_web_push_subscriptions_EndpointHash_Audience_ScopeKey",
                table: "web_push_subscriptions",
                columns: new[] { "EndpointHash", "Audience", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_web_push_subscriptions_EntityId_Audience_IsActive",
                table: "web_push_subscriptions",
                columns: new[] { "EntityId", "Audience", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_web_push_subscriptions_UserId_BusinessId_IsActive",
                table: "web_push_subscriptions",
                columns: new[] { "UserId", "BusinessId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_push_subscriptions");
        }
    }
}
