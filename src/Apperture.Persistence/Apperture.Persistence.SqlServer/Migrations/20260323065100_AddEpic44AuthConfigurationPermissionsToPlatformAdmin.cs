using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>Grants platform administrators read/manage permissions for Epic 44 authentication administration.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260323065100_AddEpic44AuthConfigurationPermissionsToPlatformAdmin")]
public partial class AddEpic44AuthConfigurationPermissionsToPlatformAdmin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var sql = """
            UPDATE [Roles]
            SET [PermissionsJson] = JSON_MODIFY(
                JSON_MODIFY(
                    [PermissionsJson],
                    'append $',
                    JSON_QUERY(N'{"action":"auth.configuration.read","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}')),
                'append $',
                JSON_QUERY(N'{"action":"auth.entra.manage","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}'))
            WHERE [Id] = N'role-platform-administrator';
            """;
        migrationBuilder.Sql(sql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
