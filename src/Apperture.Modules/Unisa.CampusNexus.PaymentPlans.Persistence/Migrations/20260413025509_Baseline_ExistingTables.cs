using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Unisa.CampusNexus.PaymentPlans.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline_ExistingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tables may already exist (created by historical platform migrations). Use idempotent DDL so
            // existing and greenfield databases both converge on this module migration history.
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[UnisaFileImportWatermarks]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[UnisaFileImportWatermarks] (
                        [SourceKey] nvarchar(128) NOT NULL,
                        [LastWriteTimeUtc] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_UnisaFileImportWatermarks] PRIMARY KEY ([SourceKey])
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[UnisaFundSourceCampusMaps]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[UnisaFundSourceCampusMaps] (
                        [Id] int NOT NULL IDENTITY,
                        [SchoolFundId] nvarchar(32) NOT NULL,
                        [CampusId] int NOT NULL,
                        [FundSourceIdsCsv] nvarchar(2048) NULL,
                        CONSTRAINT [PK_UnisaFundSourceCampusMaps] PRIMARY KEY ([Id])
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_UnisaFundSourceCampusMaps_SchoolFundId'
                      AND object_id = OBJECT_ID(N'[dbo].[UnisaFundSourceCampusMaps]'))
                BEGIN
                    CREATE UNIQUE INDEX [IX_UnisaFundSourceCampusMaps_SchoolFundId]
                    ON [dbo].[UnisaFundSourceCampusMaps] ([SchoolFundId]);
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[UnisaPaymentPlanArrangements]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[UnisaPaymentPlanArrangements] (
                        [ArrangementId] uniqueidentifier NOT NULL,
                        [ExternalKey] nvarchar(256) NOT NULL,
                        [SchoolFundId] nvarchar(32) NOT NULL,
                        [CnsStudentId] int NULL,
                        [StudentNumber] nvarchar(64) NULL,
                        [InSchoolPaymentPlanId] int NULL,
                        [OutOfSchoolPaymentPlanId] int NULL,
                        [SourceKind] nvarchar(64) NOT NULL,
                        [IsCurrent] bit NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [SupersededAt] datetimeoffset NULL,
                        CONSTRAINT [PK_UnisaPaymentPlanArrangements] PRIMARY KEY ([ArrangementId])
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_UnisaPaymentPlanArrangements_ExternalKey_SchoolFundId_IsCurrent'
                      AND object_id = OBJECT_ID(N'[dbo].[UnisaPaymentPlanArrangements]'))
                BEGIN
                    CREATE INDEX [IX_UnisaPaymentPlanArrangements_ExternalKey_SchoolFundId_IsCurrent]
                    ON [dbo].[UnisaPaymentPlanArrangements] ([ExternalKey], [SchoolFundId], [IsCurrent]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Module baseline is not reversed automatically; tables remain for data safety.
        }
    }
}
