using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>SWM-6: SchedulerDispatchHeartbeats for WorkerHost timer-loop visibility.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260714190000_SWM6_SchedulerDispatchHeartbeat")]
public partial class SWM6_SchedulerDispatchHeartbeat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SchedulerDispatchHeartbeats",
            columns: table => new
            {
                RuntimePlaneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                HostId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PollIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                LastCycleStartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastCycleCompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastSuccessfulCycleUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastErrorSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                AssignmentCount = table.Column<int>(type: "int", nullable: false),
                DueCount = table.Column<int>(type: "int", nullable: false),
                StaleCount = table.Column<int>(type: "int", nullable: false),
                CandidateCount = table.Column<int>(type: "int", nullable: false),
                ClaimedCount = table.Column<int>(type: "int", nullable: false),
                DispatchedCount = table.Column<int>(type: "int", nullable: false),
                SkippedCount = table.Column<int>(type: "int", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SchedulerDispatchHeartbeats", x => x.RuntimePlaneId);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SchedulerDispatchHeartbeats");
    }
}
