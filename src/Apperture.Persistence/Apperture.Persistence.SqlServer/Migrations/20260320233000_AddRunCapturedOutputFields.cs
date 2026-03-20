using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRunCapturedOutputFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapturedOutputJson",
                table: "OperationRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapturedOutputStatus",
                table: "OperationRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapturedOutputSummary",
                table: "OperationRuns",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapturedOutputVersion",
                table: "OperationRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapturedOutputJson",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "CapturedOutputStatus",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "CapturedOutputSummary",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "CapturedOutputVersion",
                table: "OperationRuns");
        }
    }
}
