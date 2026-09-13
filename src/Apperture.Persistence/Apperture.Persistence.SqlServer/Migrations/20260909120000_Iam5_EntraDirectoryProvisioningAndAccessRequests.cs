using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Entra directory provisioning access-request policy flags and pending-request persistence.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260909120000_Iam5_EntraDirectoryProvisioningAndAccessRequests")]
public partial class Iam5_EntraDirectoryProvisioningAndAccessRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllowGuestAccessRequests",
            table: "EntraInstanceRegistrations",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "AllowMemberAccessRequests",
            table: "EntraInstanceRegistrations",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "EntraAccessRequests",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RegistrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProviderObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DisplayNameSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                UserPrincipalNameSnapshot = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                MailSnapshot = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                UserTypeSnapshot = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                AuthenticatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReviewedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProvisionedUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EntraAccessRequests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EntraAccessRequests_Status",
            table: "EntraAccessRequests",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_EntraAccessRequests_RegistrationId_TenantId_ProviderObjectId_Status",
            table: "EntraAccessRequests",
            columns: new[] { "RegistrationId", "TenantId", "ProviderObjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_EntraAccessRequests_PendingIdentity",
            table: "EntraAccessRequests",
            columns: new[] { "RegistrationId", "TenantId", "ProviderObjectId" },
            unique: true,
            filter: "[Status] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EntraAccessRequests");
        migrationBuilder.DropColumn(name: "AllowGuestAccessRequests", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "AllowMemberAccessRequests", table: "EntraInstanceRegistrations");
    }
}
