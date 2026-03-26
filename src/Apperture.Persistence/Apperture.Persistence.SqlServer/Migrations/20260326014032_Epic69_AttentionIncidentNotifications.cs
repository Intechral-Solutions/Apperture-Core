using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic69_AttentionIncidentNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttentionIncidentNotifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AttentionIncidentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConditionKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NotificationKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SuppressionReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SubjectSnapshot = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionIncidentNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidentNotifications_AttentionIncidentId_CreatedAtUtc",
                table: "AttentionIncidentNotifications",
                columns: new[] { "AttentionIncidentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidentNotifications_AttentionIncidentId_NotificationKind",
                table: "AttentionIncidentNotifications",
                columns: new[] { "AttentionIncidentId", "NotificationKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttentionIncidentNotifications");
        }
    }
}
