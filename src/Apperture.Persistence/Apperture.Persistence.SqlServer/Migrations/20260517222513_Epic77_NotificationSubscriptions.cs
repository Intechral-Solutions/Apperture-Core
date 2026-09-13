using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_NotificationSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationDefinitionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecipientKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecipientRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DeliveryMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UpdatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSubscriptionUserOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationDefinitionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationalTeamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSubscriptionUserOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_IntegrationId",
                table: "NotificationSubscriptions",
                column: "IntegrationId",
                filter: "[IntegrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_NotificationDefinitionId",
                table: "NotificationSubscriptions",
                column: "NotificationDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_NotificationDefinitionId_IntegrationId_RecipientKind_RecipientRef",
                table: "NotificationSubscriptions",
                columns: new[] { "NotificationDefinitionId", "IntegrationId", "RecipientKind", "RecipientRef" },
                unique: true,
                filter: "[IntegrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_NotificationDefinitionId_RecipientKind_RecipientRef",
                table: "NotificationSubscriptions",
                columns: new[] { "NotificationDefinitionId", "RecipientKind", "RecipientRef" },
                unique: true,
                filter: "[IntegrationId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_RecipientKind_RecipientRef",
                table: "NotificationSubscriptions",
                columns: new[] { "RecipientKind", "RecipientRef" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptionUserOverrides_UserId_NotificationDefinitionId",
                table: "NotificationSubscriptionUserOverrides",
                columns: new[] { "UserId", "NotificationDefinitionId" },
                unique: true,
                filter: "[OperationalTeamId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptionUserOverrides_UserId_NotificationDefinitionId_OperationalTeamId",
                table: "NotificationSubscriptionUserOverrides",
                columns: new[] { "UserId", "NotificationDefinitionId", "OperationalTeamId" },
                unique: true,
                filter: "[OperationalTeamId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationSubscriptions");

            migrationBuilder.DropTable(
                name: "NotificationSubscriptionUserOverrides");
        }
    }
}
