using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationExecutionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationCancelRequests",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationCancelRequests", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "OperationInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CurrentStepName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RelatedRunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StepName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationLifecycleEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationStartRequests",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationStartRequests", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "OperationStepInstances",
                columns: table => new
                {
                    OperationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationStepInstances", x => new { x.OperationInstanceId, x.StepName, x.Attempt });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationLifecycleEvents_RelatedRunId",
                table: "OperationLifecycleEvents",
                column: "RelatedRunId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationLifecycleEvents_RunId_OccurredAt",
                table: "OperationLifecycleEvents",
                columns: new[] { "RunId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationStartRequests_IdempotencyKey",
                table: "OperationStartRequests",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationCancelRequests");

            migrationBuilder.DropTable(
                name: "OperationInstances");

            migrationBuilder.DropTable(
                name: "OperationLifecycleEvents");

            migrationBuilder.DropTable(
                name: "OperationStartRequests");

            migrationBuilder.DropTable(
                name: "OperationStepInstances");
        }
    }
}
