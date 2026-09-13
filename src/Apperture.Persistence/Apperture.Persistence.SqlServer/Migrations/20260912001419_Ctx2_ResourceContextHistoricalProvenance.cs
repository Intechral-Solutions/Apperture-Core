using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Ctx2_ResourceContextHistoricalProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "WorkflowStartRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "WorkflowInstances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "ScheduleRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "PlatformEvents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "OperationStartRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "OperationRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "OperationInstances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "OperationGroups",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "NotificationInstances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "AuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceContextId",
                table: "AttentionIncidents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_ResourceContextId",
                table: "ScheduleRuns",
                column: "ResourceContextId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_ResourceContextId_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "ResourceContextId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "[ResourceContextId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationRuns_ResourceContextId",
                table: "OperationRuns",
                column: "ResourceContextId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInstances_ResourceContextId_CreatedAtUtc",
                table: "NotificationInstances",
                columns: new[] { "ResourceContextId", "CreatedAtUtc" },
                descending: new[] { false, true },
                filter: "[ResourceContextId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ResourceContextId_At",
                table: "AuditLogs",
                columns: new[] { "ResourceContextId", "At" },
                descending: new[] { false, true },
                filter: "[ResourceContextId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionIncidents_ResourceContextId",
                table: "AttentionIncidents",
                column: "ResourceContextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduleRuns_ResourceContextId",
                table: "ScheduleRuns");

            migrationBuilder.DropIndex(
                name: "IX_PlatformEvents_ResourceContextId_OccurredAtUtc",
                table: "PlatformEvents");

            migrationBuilder.DropIndex(
                name: "IX_OperationRuns_ResourceContextId",
                table: "OperationRuns");

            migrationBuilder.DropIndex(
                name: "IX_NotificationInstances_ResourceContextId_CreatedAtUtc",
                table: "NotificationInstances");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ResourceContextId_At",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttentionIncidents_ResourceContextId",
                table: "AttentionIncidents");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "WorkflowStartRequests");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "ScheduleRuns");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "PlatformEvents");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "OperationStartRequests");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "OperationInstances");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "OperationGroups");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "NotificationInstances");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ResourceContextId",
                table: "AttentionIncidents");
        }
    }
}
