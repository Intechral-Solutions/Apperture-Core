using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformIdentityTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LastAuthenticatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsBreakGlassEligible = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_Source_ExternalId",
                table: "IdentityLinks",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            // Seed platform Administrator role (minimal permission set for foundation).
            var adminRoleId = "role-platform-administrator";
            var adminPermissionsJson = "[{\"action\":\"connection.create\",\"resource\":{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}},{\"action\":\"connection.read\",\"resource\":{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}},{\"action\":\"connection.update\",\"resource\":{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}},{\"action\":\"connection.delete\",\"resource\":{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}},{\"action\":\"binding.assign\",\"resource\":{\"resourceType\":\"Binding\",\"scopeKey\":\"instance\"}},{\"action\":\"binding.read\",\"resource\":{\"resourceType\":\"Binding\",\"scopeKey\":\"instance\"}},{\"action\":\"binding.create\",\"resource\":{\"resourceType\":\"Binding\",\"scopeKey\":\"instance\"}},{\"action\":\"binding.delete\",\"resource\":{\"resourceType\":\"Binding\",\"scopeKey\":\"instance\"}},{\"action\":\"binding.revoke\",\"resource\":{\"resourceType\":\"Binding\",\"scopeKey\":\"instance\"}},{\"action\":\"role.manage\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]";
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name", "Description", "PermissionsJson", "IsSystemRole" },
                values: new object[] { adminRoleId, "Platform Administrator", "Full platform administration (Epic 1 seed).", adminPermissionsJson, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserRoles");
            migrationBuilder.DropTable(name: "IdentityLinks");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
