using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddEntraInstanceRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntraInstanceRegistrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UsesDeploymentSecret = table.Column<bool>(type: "bit", nullable: false),
                    CallbackPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntraInstanceRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntraInstanceRegistrations_IsActive",
                table: "EntraInstanceRegistrations",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntraInstanceRegistrations");
        }
    }
}
