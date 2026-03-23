using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>Grants platform administrators permission to edit sign-in UX policy.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260323053000_AddAuthUiPolicyPermissionToPlatformAdmin")]
public partial class AddAuthUiPolicyPermissionToPlatformAdmin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var sql = """
            UPDATE [Roles]
            SET [PermissionsJson] = JSON_MODIFY(
                [PermissionsJson],
                'append $',
                JSON_QUERY(N'{"action":"auth.ui_policy.manage","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}'))
            WHERE [Id] = N'role-platform-administrator';
            """;
        migrationBuilder.Sql(sql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
