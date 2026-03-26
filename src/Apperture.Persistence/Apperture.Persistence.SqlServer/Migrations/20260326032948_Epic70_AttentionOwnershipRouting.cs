using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic70_AttentionOwnershipRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EscalationThresholdApplied",
                table: "AttentionIncidentNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientResolvedFrom",
                table: "AttentionIncidentNotifications",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuppressedDueToAcknowledgment",
                table: "AttentionIncidentNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WasEscalation",
                table: "AttentionIncidentNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttentionRoutingRecipients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InstanceAttentionRoutingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IntegrationAttentionRoutingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionRoutingRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstanceAttentionRoutings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConditionKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SendResolvedNotification = table.Column<bool>(type: "bit", nullable: false),
                    SuppressRemindersWhenAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    SuppressEscalationWhenAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    EscalationAfterTicks = table.Column<long>(type: "bigint", nullable: true),
                    ReminderIntervalTicks = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceAttentionRoutings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationAttentionRoutings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConditionKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SendResolvedNotification = table.Column<bool>(type: "bit", nullable: false),
                    SuppressRemindersWhenAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    SuppressEscalationWhenAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    EscalationAfterTicks = table.Column<long>(type: "bigint", nullable: true),
                    ReminderIntervalTicks = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationAttentionRoutings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionRoutingRecipients_InstanceAttentionRoutingId",
                table: "AttentionRoutingRecipients",
                column: "InstanceAttentionRoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionRoutingRecipients_IntegrationAttentionRoutingId",
                table: "AttentionRoutingRecipients",
                column: "IntegrationAttentionRoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceAttentionRoutings_ConditionKey",
                table: "InstanceAttentionRoutings",
                column: "ConditionKey");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationAttentionRoutings_IntegrationId_ConditionKey",
                table: "IntegrationAttentionRoutings",
                columns: new[] { "IntegrationId", "ConditionKey" },
                unique: true);
            var reminder5Min = TimeSpan.FromMinutes(5).Ticks;
            migrationBuilder.InsertData(
                table: "InstanceAttentionRoutings",
                columns: new[]
                {
                    "Id",
                    "ConditionKey",
                    "IsEnabled",
                    "SendResolvedNotification",
                    "SuppressRemindersWhenAcknowledged",
                    "SuppressEscalationWhenAcknowledged",
                    "EscalationAfterTicks",
                    "ReminderIntervalTicks",
                },
                values: new object[,]
                {
                    {
                        "instance_default",
                        "expected_run_missing",
                        true,
                        true,
                        true,
                        true,
                        null,
                        reminder5Min,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttentionRoutingRecipients");

            migrationBuilder.DropTable(
                name: "InstanceAttentionRoutings");

            migrationBuilder.DropTable(
                name: "IntegrationAttentionRoutings");

            migrationBuilder.DropColumn(
                name: "EscalationThresholdApplied",
                table: "AttentionIncidentNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientResolvedFrom",
                table: "AttentionIncidentNotifications");

            migrationBuilder.DropColumn(
                name: "SuppressedDueToAcknowledgment",
                table: "AttentionIncidentNotifications");

            migrationBuilder.DropColumn(
                name: "WasEscalation",
                table: "AttentionIncidentNotifications");
        }
    }
}
