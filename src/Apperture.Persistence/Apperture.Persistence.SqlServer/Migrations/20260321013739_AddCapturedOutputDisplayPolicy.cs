using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCapturedOutputDisplayPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclaredScopesJson",
                table: "RuntimeIdentities",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CapturedOutputDisplayPolicy",
                table: "OperationRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentId",
                table: "EnrollmentTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopesJson",
                table: "EnrollmentTokens",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RuntimeControlRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuntimeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_RuntimeControlRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlRequests_RuntimeId_ControlKind",
                table: "RuntimeControlRequests",
                columns: new[] { "RuntimeId", "ControlKind" },
                unique: true,
                filter: "[Status] = 'queued'");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlRequests_RuntimeId_RequestedAt",
                table: "RuntimeControlRequests",
                columns: new[] { "RuntimeId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlRequests_Status",
                table: "RuntimeControlRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeControlRequests");

            migrationBuilder.DropColumn(
                name: "DeclaredScopesJson",
                table: "RuntimeIdentities");

            migrationBuilder.DropColumn(
                name: "CapturedOutputDisplayPolicy",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "EnrollmentTokens");

            migrationBuilder.DropColumn(
                name: "ScopesJson",
                table: "EnrollmentTokens");
        }
    }
}
