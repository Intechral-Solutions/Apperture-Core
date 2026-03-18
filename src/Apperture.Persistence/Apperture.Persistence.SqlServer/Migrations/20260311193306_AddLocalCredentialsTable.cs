using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCredentialsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalCredentials",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Salt = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalCredentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_LocalCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalCredentials");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles");
        }
    }
}
