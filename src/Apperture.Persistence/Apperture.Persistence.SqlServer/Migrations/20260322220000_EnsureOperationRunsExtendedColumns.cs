using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <summary>
    /// Ensures <c>OperationRuns</c> has captured-output and failure-diagnostics columns when the DB
    /// was created or migrated without them (e.g. migration history out of sync). Idempotent via <c>COL_LENGTH</c>.
    /// </summary>
    public partial class EnsureOperationRunsExtendedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.OperationRuns', N'CapturedOutputJson') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [CapturedOutputJson] nvarchar(max) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'CapturedOutputStatus') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [CapturedOutputStatus] nvarchar(32) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'CapturedOutputSummary') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [CapturedOutputSummary] nvarchar(512) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'CapturedOutputVersion') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [CapturedOutputVersion] int NOT NULL CONSTRAINT [DF_OperationRuns_CapturedOutputVersion] DEFAULT 0;

                IF COL_LENGTH(N'dbo.OperationRuns', N'CapturedOutputDisplayPolicy') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [CapturedOutputDisplayPolicy] nvarchar(32) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'FailureDiagnosticsDisplayPolicy') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [FailureDiagnosticsDisplayPolicy] nvarchar(32) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'FailureDiagnosticsJson') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [FailureDiagnosticsJson] nvarchar(max) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'FailureDiagnosticsStatus') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [FailureDiagnosticsStatus] nvarchar(32) NULL;

                IF COL_LENGTH(N'dbo.OperationRuns', N'FailureDiagnosticsVersion') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [FailureDiagnosticsVersion] int NOT NULL CONSTRAINT [DF_OperationRuns_FailureDiagnosticsVersion] DEFAULT 0;

                IF COL_LENGTH(N'dbo.OperationRuns', N'FailureSummary') IS NULL
                    ALTER TABLE [dbo].[OperationRuns] ADD [FailureSummary] nvarchar(512) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Repair migration: columns are part of the current model; no automatic rollback.
        }
    }
}
