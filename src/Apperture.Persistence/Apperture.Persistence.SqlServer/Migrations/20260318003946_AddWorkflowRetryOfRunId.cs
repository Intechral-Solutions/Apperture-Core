using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowRetryOfRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetryOfRunId",
                table: "OperationRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationRuns_RetryOfRunId",
                table: "OperationRuns",
                column: "RetryOfRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OperationRuns_RetryOfRunId",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "RetryOfRunId",
                table: "OperationRuns");
        }
    }
}
