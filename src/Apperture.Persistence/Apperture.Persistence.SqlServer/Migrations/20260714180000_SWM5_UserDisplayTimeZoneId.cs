using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>SWM-5: nullable Users.DisplayTimeZoneId for future operator display preference.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260714180000_SWM5_UserDisplayTimeZoneId")]
public partial class SWM5_UserDisplayTimeZoneId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DisplayTimeZoneId",
            table: "Users",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DisplayTimeZoneId",
            table: "Users");
    }
}
