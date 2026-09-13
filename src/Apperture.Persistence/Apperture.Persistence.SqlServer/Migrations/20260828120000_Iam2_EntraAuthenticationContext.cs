using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-2: optional Microsoft Entra Conditional Access authentication-context identifier on the
/// instance registration. Null means strong-authentication step-up is not configured.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260828120000_Iam2_EntraAuthenticationContext")]
public partial class Iam2_EntraAuthenticationContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AuthenticationContextId",
            table: "EntraInstanceRegistrations",
            type: "nvarchar(8)",
            maxLength: 8,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AuthenticationContextId",
            table: "EntraInstanceRegistrations");
    }
}
