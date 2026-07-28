using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFounderProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerInstructions",
                table: "businesses",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "businesses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "businesses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicEmail",
                table: "businesses",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferencePoint",
                table: "businesses",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "businesses",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortDescription",
                table: "businesses",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedForReviewAtUtc",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "access_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Grant = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_invitations_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    ByteSize = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_images", x => x.Id);
                    table.CheckConstraint("ck_business_image_order", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("ck_business_image_size", "\"Width\" > 0 AND \"Height\" > 0 AND \"ByteSize\" > 0");
                    table.ForeignKey(
                        name: "FK_business_images_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_status_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_status_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_status_changes_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_access_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Entity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_access_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_invitations_BusinessId_CreatedAtUtc",
                table: "access_invitations",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_access_invitations_Email_ExpiresAtUtc",
                table: "access_invitations",
                columns: new[] { "Email", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_access_invitations_TokenHash",
                table: "access_invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_images_BusinessId_Kind",
                table: "business_images",
                columns: new[] { "BusinessId", "Kind" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"Kind\" IN ('Logo', 'Cover')");

            migrationBuilder.CreateIndex(
                name: "IX_business_images_BusinessId_Kind_IsDeleted",
                table: "business_images",
                columns: new[] { "BusinessId", "Kind", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_business_images_StorageKey",
                table: "business_images",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_status_changes_BusinessId_OccurredAtUtc",
                table: "business_status_changes",
                columns: new[] { "BusinessId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_access_audits_BusinessId_OccurredAtUtc",
                table: "platform_access_audits",
                columns: new[] { "BusinessId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_access_audits_OccurredAtUtc",
                table: "platform_access_audits",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_invitations");

            migrationBuilder.DropTable(
                name: "business_images");

            migrationBuilder.DropTable(
                name: "business_status_changes");

            migrationBuilder.DropTable(
                name: "platform_access_audits");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "CustomerInstructions",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "PublicEmail",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "ReferencePoint",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "ShortDescription",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "SubmittedForReviewAtUtc",
                table: "businesses");
        }
    }
}
