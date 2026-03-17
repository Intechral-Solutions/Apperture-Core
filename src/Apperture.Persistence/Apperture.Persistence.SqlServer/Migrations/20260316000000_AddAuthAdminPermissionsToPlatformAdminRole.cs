using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthAdminPermissionsToPlatformAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append Epic 3 auth-admin permissions to the platform administrator role.
            // JSON_MODIFY(..., 'append $', JSON_QUERY(...)) appends one object to the array.
            var sql = """
                UPDATE [Roles]
                SET [PermissionsJson] = JSON_MODIFY(
                    JSON_MODIFY(
                        JSON_MODIFY(
                            JSON_MODIFY([PermissionsJson], 'append $', JSON_QUERY(N'{""action"":""user.read"",""resource"":{""resourceType"":""User"",""scopeKey"":""instance""}}')),
                            'append $', JSON_QUERY(N'{""action"":""user.manage"",""resource"":{""resourceType"":""User"",""scopeKey"":""instance""}}')),
                        'append $', JSON_QUERY(N'{""action"":""role.assign"",""resource"":{""resourceType"":""Role"",""scopeKey"":""instance""}}')),
                    'append $', JSON_QUERY(N'{""action"":""credential.manage"",""resource"":{""resourceType"":""User"",""scopeKey"":""instance""}}'))
                WHERE [Id] = N'role-platform-administrator';
                """;
            migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing specific array elements by index would require parsing and rebuilding.
            // Down is a no-op; revert by re-running schema/data migrations if needed.
        }
    }
}
