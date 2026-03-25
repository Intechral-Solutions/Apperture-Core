using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic66_ScheduleLifecycleEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastScheduleDisabledAtUtc",
                table: "ScheduleDefinitions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastScheduleEnabledAtUtc",
                table: "ScheduleDefinitions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MaterialScheduleSemanticsChangedAtUtc",
                table: "ScheduleDefinitions",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastScheduleDisabledAtUtc",
                table: "ScheduleDefinitions");

            migrationBuilder.DropColumn(
                name: "LastScheduleEnabledAtUtc",
                table: "ScheduleDefinitions");

            migrationBuilder.DropColumn(
                name: "MaterialScheduleSemanticsChangedAtUtc",
                table: "ScheduleDefinitions");
        }
    }
}
