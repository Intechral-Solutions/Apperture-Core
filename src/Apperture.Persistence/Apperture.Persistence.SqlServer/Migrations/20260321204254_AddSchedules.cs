using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RecurrenceExpression = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    TargetJobType = table.Column<int>(type: "int", nullable: false),
                    TargetJobConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionPolicyRef = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScheduleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuntimePlaneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PolicyOverrides = table.Column<string>(type: "nvarchar(4096)", maxLength: 4096, nullable: true),
                    DesiredVersion = table.Column<int>(type: "int", nullable: false),
                    AppliedVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleAssignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_RuntimePlaneId",
                table: "ScheduleAssignments",
                column: "RuntimePlaneId");

            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssignmentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FireTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutcomeReason = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_AssignmentId",
                table: "ScheduleRuns",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_ScheduleId_FireTimeUtc",
                table: "ScheduleRuns",
                columns: new[] { "ScheduleId", "FireTimeUtc" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "ScheduleStates",
                columns: table => new
                {
                    AssignmentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NextDueUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOutcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastOutcomeReason = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LastSuccessfulDispatchUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleStates", x => x.AssignmentId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduleStates");
            migrationBuilder.DropTable(name: "ScheduleRuns");
            migrationBuilder.DropTable(name: "ScheduleAssignments");
            migrationBuilder.DropTable(name: "ScheduleDefinitions");
        }
    }
}
