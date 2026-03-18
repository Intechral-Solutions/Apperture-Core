using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowExternalControlRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowExternalControlRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ControlKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExternalControlRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExternalControlRequests_RunId_ControlKind",
                table: "WorkflowExternalControlRequests",
                columns: new[] { "RunId", "ControlKind" },
                unique: true,
                filter: "[Status] = 'queued'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExternalControlRequests_RunId_RequestedAt",
                table: "WorkflowExternalControlRequests",
                columns: new[] { "RunId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExternalControlRequests_Status",
                table: "WorkflowExternalControlRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowExternalControlRequests");
        }
    }
}
