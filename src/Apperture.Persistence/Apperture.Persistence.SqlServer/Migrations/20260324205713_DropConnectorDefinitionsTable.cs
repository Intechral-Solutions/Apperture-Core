using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class DropConnectorDefinitionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorDefinitions");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "ScheduleDefinitions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "Bindings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "ScheduleDefinitions");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "Bindings");

            migrationBuilder.CreateTable(
                name: "ConnectorDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequiredInputSchemaJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorDefinitions", x => x.Id);
                });
        }
    }
}
