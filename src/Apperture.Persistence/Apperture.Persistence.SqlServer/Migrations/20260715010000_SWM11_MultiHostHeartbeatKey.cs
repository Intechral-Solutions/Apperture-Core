using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>SWM-11: heartbeat identity is (RuntimePlaneId, HostId) for multi-host observation.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260715010000_SWM11_MultiHostHeartbeatKey")]
public partial class SWM11_MultiHostHeartbeatKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Normalize blank host ids so composite PK is well-formed.
        migrationBuilder.Sql(
            """
            UPDATE SchedulerDispatchHeartbeats
            SET HostId = N'unknown'
            WHERE HostId IS NULL OR LTRIM(RTRIM(HostId)) = N'';
            """);

        migrationBuilder.DropPrimaryKey(
            name: "PK_SchedulerDispatchHeartbeats",
            table: "SchedulerDispatchHeartbeats");

        migrationBuilder.AddPrimaryKey(
            name: "PK_SchedulerDispatchHeartbeats",
            table: "SchedulerDispatchHeartbeats",
            columns: new[] { "RuntimePlaneId", "HostId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Collapsing to plane-only PK cannot preserve multiple hosts safely.
        // Keep the newest row per plane and drop the rest before restoring the PK.
        migrationBuilder.Sql(
            """
            ;WITH ranked AS (
                SELECT RuntimePlaneId, HostId,
                       ROW_NUMBER() OVER (PARTITION BY RuntimePlaneId ORDER BY UpdatedUtc DESC) AS rn
                FROM SchedulerDispatchHeartbeats
            )
            DELETE FROM ranked WHERE rn > 1;
            """);

        migrationBuilder.DropPrimaryKey(
            name: "PK_SchedulerDispatchHeartbeats",
            table: "SchedulerDispatchHeartbeats");

        migrationBuilder.AddPrimaryKey(
            name: "PK_SchedulerDispatchHeartbeats",
            table: "SchedulerDispatchHeartbeats",
            column: "RuntimePlaneId");
    }
}
