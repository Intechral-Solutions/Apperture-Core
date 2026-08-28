using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-1: Entra identity-link metadata. Every added column is nullable except <c>LinkedAt</c>, which is
/// backfilled with the current UTC time for existing rows so no data migration is required. Tenant and
/// registration provenance stay null for local links and for Entra links created before IAM-1; a legacy
/// link's tenant is completed on its next successful Entra authentication.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260828090000_Iam1_EntraIdentityLinkMetadata")]
public partial class Iam1_EntraIdentityLinkMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TenantId",
            table: "IdentityLinks",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RegistrationId",
            table: "IdentityLinks",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LinkedAt",
            table: "IdentityLinks",
            type: "datetimeoffset",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AddColumn<string>(
            name: "ProviderSubject",
            table: "IdentityLinks",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DisplayNameSnapshot",
            table: "IdentityLinks",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UserPrincipalNameSnapshot",
            table: "IdentityLinks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MailSnapshot",
            table: "IdentityLinks",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MailSnapshot", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "UserPrincipalNameSnapshot", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "DisplayNameSnapshot", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "ProviderSubject", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "LinkedAt", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "RegistrationId", table: "IdentityLinks");
        migrationBuilder.DropColumn(name: "TenantId", table: "IdentityLinks");
    }
}
