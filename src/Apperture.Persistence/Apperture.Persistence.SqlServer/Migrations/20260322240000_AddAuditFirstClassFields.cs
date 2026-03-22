using System;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>Epic 37: First-class Outcome, ModuleId, ScheduleId, RunId columns and indexes.</summary>
    [DbContext(typeof(AppertureDbContext))]
    [Migration("20260322240000_AddAuditFirstClassFields")]
    public partial class AddAuditFirstClassFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModuleId",
                table: "AuditLogs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleId",
                table: "AuditLogs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunId",
                table: "AuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At_Id",
                table: "AuditLogs",
                columns: new[] { "At", "Id" },
                descending: new[] { true, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Outcome_At",
                table: "AuditLogs",
                columns: new[] { "Outcome", "At" },
                descending: new[] { false, true },
                filter: "[Outcome] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ModuleId_At",
                table: "AuditLogs",
                columns: new[] { "ModuleId", "At" },
                descending: new[] { false, true },
                filter: "[ModuleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ScheduleId_At",
                table: "AuditLogs",
                columns: new[] { "ScheduleId", "At" },
                descending: new[] { false, true },
                filter: "[ScheduleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_RunId_At",
                table: "AuditLogs",
                columns: new[] { "RunId", "At" },
                descending: new[] { false, true },
                filter: "[RunId] IS NOT NULL");

            // Backfill: extract Outcome from Details (first segment before ";")
            migrationBuilder.Sql(@"
                UPDATE a SET Outcome = seg.FirstSegment
                FROM AuditLogs a
                CROSS APPLY (
                    SELECT LTRIM(RTRIM(LEFT(a.Details, CASE WHEN CHARINDEX(';', ISNULL(a.Details, '')) > 0
                        THEN CHARINDEX(';', a.Details) - 1 ELSE LEN(a.Details) END))) AS FirstSegment
                ) seg
                WHERE a.Outcome IS NULL AND a.Details IS NOT NULL AND LEN(seg.FirstSegment) > 0 AND LEN(seg.FirstSegment) < 128
                    AND seg.FirstSegment NOT LIKE 'Resource=%' AND seg.FirstSegment NOT LIKE 'runId=%'
                    AND seg.FirstSegment NOT LIKE 'schedule:%' AND seg.FirstSegment NOT LIKE 'definitionId=%'
                    AND seg.FirstSegment NOT LIKE 'workflowId=%' AND seg.FirstSegment NOT LIKE 'operationDefinitionId=%'
                    AND seg.FirstSegment NOT LIKE 'module=%' AND seg.FirstSegment NOT LIKE 'runtimePlaneId=%'
            ");

            // Backfill: extract ModuleId from Details (module=value)
            migrationBuilder.Sql(@"
                UPDATE a SET ModuleId = LTRIM(RTRIM(SUBSTRING(a.Details, p.Pos + 7,
                    CASE WHEN CHARINDEX(';', a.Details, p.Pos + 7) > 0
                        THEN CHARINDEX(';', a.Details, p.Pos + 7) - p.Pos - 7
                        ELSE 128 END)))
                FROM AuditLogs a
                CROSS APPLY (SELECT NULLIF(PATINDEX('%module=%', a.Details), 0) AS Pos) p
                WHERE p.Pos > 0 AND a.ModuleId IS NULL
            ");

            // Backfill: extract ScheduleId from Details (schedule:value)
            migrationBuilder.Sql(@"
                UPDATE a SET ScheduleId = LTRIM(RTRIM(SUBSTRING(a.Details, p.Pos + 9,
                    CASE WHEN CHARINDEX(';', a.Details, p.Pos + 9) > 0
                        THEN CHARINDEX(';', a.Details, p.Pos + 9) - p.Pos - 9
                        ELSE 128 END)))
                FROM AuditLogs a
                CROSS APPLY (SELECT NULLIF(PATINDEX('%schedule:%', a.Details), 0) AS Pos) p
                WHERE p.Pos > 0 AND a.ScheduleId IS NULL
            ");

            // Backfill: extract RunId from Details (runId= or RunId=, else originalRunId=)
            migrationBuilder.Sql(@"
                UPDATE a SET RunId = LTRIM(RTRIM(SUBSTRING(a.Details, p.Pos + 6,
                    CASE WHEN CHARINDEX(';', a.Details, p.Pos + 6) > 0
                        THEN CHARINDEX(';', a.Details, p.Pos + 6) - p.Pos - 6
                        ELSE 64 END)))
                FROM AuditLogs a
                CROSS APPLY (SELECT COALESCE(NULLIF(PATINDEX('%runId=%', a.Details), 0), NULLIF(PATINDEX('%RunId=%', a.Details), 0)) AS Pos) p
                WHERE a.RunId IS NULL AND a.Details IS NOT NULL AND p.Pos > 0
            ");
            migrationBuilder.Sql(@"
                UPDATE a SET RunId = LTRIM(RTRIM(SUBSTRING(a.Details, p.Pos + 14,
                    CASE WHEN CHARINDEX(';', a.Details, p.Pos + 14) > 0
                        THEN CHARINDEX(';', a.Details, p.Pos + 14) - p.Pos - 14
                        ELSE 64 END)))
                FROM AuditLogs a
                CROSS APPLY (SELECT NULLIF(PATINDEX('%originalRunId=%', a.Details), 0) AS Pos) p
                WHERE a.RunId IS NULL AND a.Details IS NOT NULL AND p.Pos > 0
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_At_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Outcome_At",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ModuleId_At",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ScheduleId_At",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_RunId_At",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ModuleId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "AuditLogs");
        }
    }
}
