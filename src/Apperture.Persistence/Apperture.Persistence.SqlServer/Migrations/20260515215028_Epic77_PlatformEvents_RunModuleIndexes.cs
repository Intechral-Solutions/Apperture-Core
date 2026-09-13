using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_PlatformEvents_RunModuleIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_ModuleId_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "ModuleId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "[ModuleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_RunId_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "RunId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "[RunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformEvents_ModuleId_OccurredAtUtc",
                table: "PlatformEvents");

            migrationBuilder.DropIndex(
                name: "IX_PlatformEvents_RunId_OccurredAtUtc",
                table: "PlatformEvents");
        }
    }
}
