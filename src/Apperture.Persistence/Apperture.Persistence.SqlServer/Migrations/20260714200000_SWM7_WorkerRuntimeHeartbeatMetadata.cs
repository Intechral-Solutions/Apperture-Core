using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>SWM-7: optional worker runtime metadata on SchedulerDispatchHeartbeats.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260714200000_SWM7_WorkerRuntimeHeartbeatMetadata")]
public partial class SWM7_WorkerRuntimeHeartbeatMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ProcessStartedUtc",
            table: "SchedulerDispatchHeartbeats",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ApplicationVersion",
            table: "SchedulerDispatchHeartbeats",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EnvironmentName",
            table: "SchedulerDispatchHeartbeats",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProcessStartedUtc",
            table: "SchedulerDispatchHeartbeats");

        migrationBuilder.DropColumn(
            name: "ApplicationVersion",
            table: "SchedulerDispatchHeartbeats");

        migrationBuilder.DropColumn(
            name: "EnvironmentName",
            table: "SchedulerDispatchHeartbeats");
    }
}
