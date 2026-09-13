using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <summary>
    /// Epic 75: phased helpers for migrating legacy schedules to integration-owned records.
    /// Adds operator-facing SQL artifacts without changing runtime behavior.
    /// </summary>
    public partial class Epic75_ScheduleIntegrationBackfillHelpers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.v_LegacyScheduleBackfillCandidates', N'V') IS NOT NULL
                    DROP VIEW dbo.v_LegacyScheduleBackfillCandidates;
                EXEC(N'
                    CREATE VIEW dbo.v_LegacyScheduleBackfillCandidates
                    AS
                    SELECT
                        sd.Id,
                        sd.Name,
                        sd.TargetJobType,
                        sd.TargetJobConfigurationJson,
                        sa.RuntimePlaneId,
                        ss.LastAttemptedUtc,
                        ss.LastOutcome,
                        ss.LastOutcomeReason
                    FROM dbo.ScheduleDefinitions sd
                    LEFT JOIN dbo.ScheduleAssignments sa ON sa.ScheduleId = sd.Id
                    LEFT JOIN dbo.ScheduleStates ss ON ss.AssignmentId = sa.Id
                    WHERE sd.IntegrationId IS NULL
                ');

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ScheduleDefinitions_IntegrationId'
                      AND object_id = OBJECT_ID(N'dbo.ScheduleDefinitions', N'U')
                )
                BEGIN
                    CREATE INDEX IX_ScheduleDefinitions_IntegrationId
                        ON dbo.ScheduleDefinitions (IntegrationId)
                        WHERE IntegrationId IS NOT NULL;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.v_LegacyScheduleBackfillCandidates', N'V') IS NOT NULL
                    DROP VIEW dbo.v_LegacyScheduleBackfillCandidates;
                """);
        }
    }
}
