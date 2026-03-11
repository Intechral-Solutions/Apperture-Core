using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformIdentityReferentialIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add RowVersion to Roles for concurrency.
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Roles",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            // Unique index on Role name.
            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            // FK: IdentityLinks.UserId -> Users.Id (Restrict).
            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_UserId",
                table: "IdentityLinks",
                column: "UserId");
            migrationBuilder.AddForeignKey(
                name: "FK_IdentityLinks_Users_UserId",
                table: "IdentityLinks",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // FK: UserRoles.UserId -> Users.Id (Restrict).
            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // FK: UserRoles.RoleId -> Roles.Id (Restrict).
            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Roles_RoleId",
                table: "UserRoles",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_IdentityLinks_Users_UserId", table: "IdentityLinks");
            migrationBuilder.DropIndex(name: "IX_IdentityLinks_UserId", table: "IdentityLinks");

            migrationBuilder.DropForeignKey(name: "FK_UserRoles_Users_UserId", table: "UserRoles");
            migrationBuilder.DropForeignKey(name: "FK_UserRoles_Roles_RoleId", table: "UserRoles");

            migrationBuilder.DropIndex(name: "IX_Roles_Name", table: "Roles");
            migrationBuilder.DropColumn(name: "RowVersion", table: "Roles");
        }
    }
}
