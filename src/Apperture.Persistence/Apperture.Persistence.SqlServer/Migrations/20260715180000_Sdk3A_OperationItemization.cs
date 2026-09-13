using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260715180000_Sdk3A_OperationItemization")]
public partial class Sdk3A_OperationItemization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OperationGroups",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ProcedureId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                OperationKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TotalCount = table.Column<int>(type: "int", nullable: false),
                SucceededCount = table.Column<int>(type: "int", nullable: false),
                FailedCount = table.Column<int>(type: "int", nullable: false),
                SkippedCount = table.Column<int>(type: "int", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                SummaryJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                // SQL Server: nvarchar(n) allows n <= 4000; use max for 4096 logical app cap.
                MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OperationGroups", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OperationItems",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OperationGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                SubjectKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                DisplayLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                IsRetryable = table.Column<bool>(type: "bit", nullable: false),
                RetryCount = table.Column<int>(type: "int", nullable: false),
                LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ChangeSummaryJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                // SQL Server: nvarchar(n) allows n <= 4000; use max for 4096 logical app cap.
                MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OperationItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_OperationItems_OperationGroups_OperationGroupId",
                    column: x => x.OperationGroupId,
                    principalTable: "OperationGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OperationGroups_RunId",
            table: "OperationGroups",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItems_RunId",
            table: "OperationItems",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItems_RunId_Status",
            table: "OperationItems",
            columns: new[] { "RunId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_OperationItems_OperationGroupId_Sequence",
            table: "OperationItems",
            columns: new[] { "OperationGroupId", "Sequence" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OperationItems");
        migrationBuilder.DropTable(name: "OperationGroups");
    }
}
