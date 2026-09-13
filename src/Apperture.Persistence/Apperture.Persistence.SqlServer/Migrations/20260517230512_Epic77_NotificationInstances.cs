using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_NotificationInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDeliveryOutbox",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RecipientDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RecipientKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RecipientSource = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationDefinitionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PlatformEventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AttentionIncidentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScheduleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InstanceDedupeKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RenderedSubject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RenderedSummary = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    RenderedBodyText = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    RenderedBodyHtml = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    RenderedActionText = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RenderedActionUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RenderedContentFieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    TemplateVersionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RenderContextJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    RecipientSnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    SuppressionReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryOutbox_NotificationInstanceId_RecipientEmail_Channel",
                table: "NotificationDeliveryOutbox",
                columns: new[] { "NotificationInstanceId", "RecipientEmail", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryOutbox_Status_NextAttemptAtUtc",
                table: "NotificationDeliveryOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInstances_AttentionIncidentId_NotificationKind",
                table: "NotificationInstances",
                columns: new[] { "AttentionIncidentId", "NotificationKind" },
                filter: "[AttentionIncidentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInstances_IntegrationId_CreatedAtUtc",
                table: "NotificationInstances",
                columns: new[] { "IntegrationId", "CreatedAtUtc" },
                descending: new[] { false, true },
                filter: "[IntegrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInstances_NotificationDefinitionId_InstanceDedupeKey",
                table: "NotificationInstances",
                columns: new[] { "NotificationDefinitionId", "InstanceDedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInstances_PlatformEventId",
                table: "NotificationInstances",
                column: "PlatformEventId",
                filter: "[PlatformEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveryOutbox");

            migrationBuilder.DropTable(
                name: "NotificationInstances");
        }
    }
}
