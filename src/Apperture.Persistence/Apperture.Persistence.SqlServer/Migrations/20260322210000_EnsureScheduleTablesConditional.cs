using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <summary>
    /// Creates schedule tables when missing. The prior <see cref="AddSchedules"/> migration was sometimes
    /// applied with an empty <c>Up()</c>, leaving databases without <c>ScheduleAssignments</c> etc.
    /// Idempotent: uses IF OBJECT_ID so safe on fresh DBs that already have tables from a full AddSchedules.
    /// </summary>
    public partial class EnsureScheduleTablesConditional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep in sync with scripts/dev/Fix-ScheduleTables.ps1
            migrationBuilder.Sql(
                """
            IF OBJECT_ID(N'dbo.ScheduleDefinitions', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ScheduleDefinitions] (
                    [Id] nvarchar(64) NOT NULL,
                    [Name] nvarchar(256) NOT NULL,
                    [RecurrenceExpression] nvarchar(256) NOT NULL,
                    [TimeZone] nvarchar(128) NOT NULL,
                    [Enabled] bit NOT NULL,
                    [TargetJobType] int NOT NULL,
                    [TargetJobConfigurationJson] nvarchar(max) NULL,
                    [ExecutionPolicyRef] nvarchar(256) NULL,
                    [Version] int NOT NULL,
                    CONSTRAINT [PK_ScheduleDefinitions] PRIMARY KEY ([Id])
                );
            END
            IF OBJECT_ID(N'dbo.ScheduleAssignments', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ScheduleAssignments] (
                    [Id] nvarchar(64) NOT NULL,
                    [ScheduleId] nvarchar(64) NOT NULL,
                    [RuntimePlaneId] nvarchar(128) NOT NULL,
                    [PolicyOverrides] nvarchar(max) NULL,
                    [DesiredVersion] int NOT NULL,
                    [AppliedVersion] int NOT NULL,
                    CONSTRAINT [PK_ScheduleAssignments] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_ScheduleAssignments_RuntimePlaneId] ON [dbo].[ScheduleAssignments] ([RuntimePlaneId]);
            END
            IF OBJECT_ID(N'dbo.ScheduleRuns', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ScheduleRuns] (
                    [Id] bigint IDENTITY(1,1) NOT NULL,
                    [ScheduleId] nvarchar(64) NOT NULL,
                    [AssignmentId] nvarchar(64) NOT NULL,
                    [FireTimeUtc] datetimeoffset NOT NULL,
                    [StartedAtUtc] datetimeoffset NOT NULL,
                    [CompletedAtUtc] datetimeoffset NULL,
                    [Outcome] nvarchar(64) NOT NULL,
                    [OutcomeReason] nvarchar(2048) NULL,
                    [RunId] nvarchar(64) NULL,
                    CONSTRAINT [PK_ScheduleRuns] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_ScheduleRuns_ScheduleId_FireTimeUtc] ON [dbo].[ScheduleRuns] ([ScheduleId], [FireTimeUtc]);
                CREATE INDEX [IX_ScheduleRuns_AssignmentId] ON [dbo].[ScheduleRuns] ([AssignmentId]);
            END
            IF OBJECT_ID(N'dbo.ScheduleStates', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ScheduleStates] (
                    [AssignmentId] nvarchar(64) NOT NULL,
                    [NextDueUtc] datetimeoffset NULL,
                    [LastAttemptedUtc] datetimeoffset NULL,
                    [LastOutcome] nvarchar(64) NULL,
                    [LastOutcomeReason] nvarchar(2048) NULL,
                    [LastSuccessfulDispatchUtc] datetimeoffset NULL,
                    CONSTRAINT [PK_ScheduleStates] PRIMARY KEY ([AssignmentId])
                );
            END
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
