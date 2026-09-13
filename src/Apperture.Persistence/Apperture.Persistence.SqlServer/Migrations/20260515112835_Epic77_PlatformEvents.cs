using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic77_PlatformEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SpecVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DataContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16384, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DedupeKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IntegrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProcedureId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScheduleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CausationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_CorrelationId",
                table: "PlatformEvents",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_IntegrationId_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "IntegrationId", "OccurredAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_RecordedAtUtc",
                table: "PlatformEvents",
                column: "RecordedAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_ScheduleId_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "ScheduleId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "[ScheduleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_Severity_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "Severity", "OccurredAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_Source_Type_DedupeKey",
                table: "PlatformEvents",
                columns: new[] { "Source", "Type", "DedupeKey" },
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEvents_Type_OccurredAtUtc",
                table: "PlatformEvents",
                columns: new[] { "Type", "OccurredAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformEvents");
        }
    }
}
