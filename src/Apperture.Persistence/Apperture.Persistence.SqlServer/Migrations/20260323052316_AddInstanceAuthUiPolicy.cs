using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceAuthUiPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstanceAuthUiPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ShowLocalSignInInPrimaryChooser = table.Column<bool>(type: "bit", nullable: false),
                    PreferEntraInChooser = table.Column<bool>(type: "bit", nullable: false),
                    AutoRedirectToEntra = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceAuthUiPolicies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "InstanceAuthUiPolicies",
                columns: new[] { "Id", "ShowLocalSignInInPrimaryChooser", "PreferEntraInChooser", "AutoRedirectToEntra" },
                values: new object[] { "instance", true, false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstanceAuthUiPolicies");
        }
    }
}
