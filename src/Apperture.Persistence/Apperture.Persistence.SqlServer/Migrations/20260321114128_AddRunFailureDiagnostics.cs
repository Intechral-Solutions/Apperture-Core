using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRunFailureDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureDiagnosticsDisplayPolicy",
                table: "OperationRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureDiagnosticsJson",
                table: "OperationRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureDiagnosticsStatus",
                table: "OperationRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureDiagnosticsVersion",
                table: "OperationRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureSummary",
                table: "OperationRuns",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureDiagnosticsDisplayPolicy",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "FailureDiagnosticsJson",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "FailureDiagnosticsStatus",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "FailureDiagnosticsVersion",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "FailureSummary",
                table: "OperationRuns");
        }
    }
}
