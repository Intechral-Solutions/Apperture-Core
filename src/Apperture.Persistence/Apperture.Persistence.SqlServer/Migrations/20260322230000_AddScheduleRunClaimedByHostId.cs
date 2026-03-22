using System;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>Epic 36: Multi-host scheduler claim ownership.</summary>
    [DbContext(typeof(AppertureDbContext))]
    [Migration("20260322230000_AddScheduleRunClaimedByHostId")]
    public partial class AddScheduleRunClaimedByHostId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimedByHostId",
                table: "ScheduleRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedByHostId",
                table: "ScheduleRuns");
        }
    }
}
