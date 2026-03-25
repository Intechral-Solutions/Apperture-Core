using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    [DbContext(typeof(AppertureDbContext))]
    [Migration("20260325110000_AddConnectionVerificationColumns")]
    public partial class AddConnectionVerificationColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastVerificationAtUtc",
                table: "Connections",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastVerificationSummary",
                table: "Connections",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastVerificationStatus",
                table: "Connections",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastVerificationAtUtc",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "LastVerificationSummary",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "LastVerificationStatus",
                table: "Connections");
        }
    }
}
