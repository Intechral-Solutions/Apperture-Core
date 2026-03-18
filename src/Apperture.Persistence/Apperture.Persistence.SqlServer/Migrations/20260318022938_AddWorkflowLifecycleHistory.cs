using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowLifecycleHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_WorkflowLifecycleEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLifecycleEvents_RelatedRunId",
                table: "WorkflowLifecycleEvents",
                column: "RelatedRunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLifecycleEvents_RunId_OccurredAt",
                table: "WorkflowLifecycleEvents",
                columns: new[] { "RunId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowLifecycleEvents");
        }
    }
}
