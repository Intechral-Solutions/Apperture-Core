using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260524124500_Epic109_AddPlatformSettingsPermissionsToPlatformAdmin")]
public partial class Epic109_AddPlatformSettingsPermissionsToPlatformAdmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var sql = """
            UPDATE [Roles]
            SET [PermissionsJson] = JSON_MODIFY(
                JSON_MODIFY(
                    [PermissionsJson],
                    'append $',
                    JSON_QUERY(N'{"action":"platform.settings.read","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}')),
                'append $',
                JSON_QUERY(N'{"action":"platform.settings.manage","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}'))
            WHERE [Id] = N'role-platform-administrator';
            """;
        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
