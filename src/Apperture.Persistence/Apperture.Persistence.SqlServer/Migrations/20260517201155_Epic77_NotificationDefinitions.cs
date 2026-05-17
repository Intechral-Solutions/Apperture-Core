using System;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_NotificationDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SourceKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Mutability = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PrimaryEventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DefaultSeverity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RuleJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    TemplateJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UpdatedByActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_IntegrationId",
                table: "NotificationDefinitions",
                column: "IntegrationId",
                filter: "[IntegrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_ModuleId",
                table: "NotificationDefinitions",
                column: "ModuleId",
                filter: "[ModuleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_Origin",
                table: "NotificationDefinitions",
                column: "Origin");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_PrimaryEventType",
                table: "NotificationDefinitions",
                column: "PrimaryEventType",
                filter: "[PrimaryEventType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_Scope_IntegrationId_DisplayName",
                table: "NotificationDefinitions",
                columns: new[] { "Scope", "IntegrationId", "DisplayName" },
                unique: true,
                filter: "[Origin] = N'userCreated'");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDefinitions_SourceKind_IsEnabled",
                table: "NotificationDefinitions",
                columns: new[] { "SourceKind", "IsEnabled" });

            var seededAt = new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                table: "NotificationDefinitions",
                columns:
                [
                    "Id", "DisplayName", "Description", "SourceKind", "Origin", "Scope", "Mutability",
                    "ModuleId", "IntegrationId", "PrimaryEventType", "IsEnabled", "DefaultSeverity",
                    "RuleJson", "TemplateJson", "CreatedAtUtc", "UpdatedAtUtc", "CreatedByActorId", "UpdatedByActorId",
                ],
                values: new object[]
                {
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    "Expected run missing",
                    "Platform built-in for schedule expectation attention (Epic 69 parity).",
                    NotificationSourceKinds.PlatformEvent,
                    NotificationDefinitionOrigins.PlatformBuiltIn,
                    NotificationDefinitionScopes.Platform,
                    NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
                    null,
                    null,
                    PlatformEventTypes.ScheduleExpectedRunMissing,
                    true,
                    "warning",
                    ExpectedRunMissingNotificationDefinitionSeed.RuleJson,
                    ExpectedRunMissingNotificationDefinitionSeed.TemplateJson,
                    seededAt,
                    seededAt,
                    null,
                    null,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDefinitions");
        }
    }
}
