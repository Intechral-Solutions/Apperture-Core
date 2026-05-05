using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic75_EntraValidationAndDirectoryDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureTenantId",
                table: "EntraInstanceRegistrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DirectoryDiscoveryStatus",
                table: "EntraInstanceRegistrations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "LastDirectoryDiscoveryDetail",
                table: "EntraInstanceRegistrations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDirectoryDiscoverySummary",
                table: "EntraInstanceRegistrations",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDirectoryDiscoveryUtc",
                table: "EntraInstanceRegistrations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastOpenIdValidationDetail",
                table: "EntraInstanceRegistrations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastOpenIdValidationSummary",
                table: "EntraInstanceRegistrations",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastOpenIdValidationUtc",
                table: "EntraInstanceRegistrations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenIdConfigurationStatus",
                table: "EntraInstanceRegistrations",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureTenantId",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "DirectoryDiscoveryStatus",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastDirectoryDiscoveryDetail",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastDirectoryDiscoverySummary",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastDirectoryDiscoveryUtc",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastOpenIdValidationDetail",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastOpenIdValidationSummary",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "LastOpenIdValidationUtc",
                table: "EntraInstanceRegistrations");

            migrationBuilder.DropColumn(
                name: "OpenIdConfigurationStatus",
                table: "EntraInstanceRegistrations");
        }
    }
}
