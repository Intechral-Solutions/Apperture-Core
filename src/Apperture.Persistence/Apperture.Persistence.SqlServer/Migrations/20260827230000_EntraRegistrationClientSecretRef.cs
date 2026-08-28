using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Stores an opaque ISecretStore reference for the Entra confidential client secret.
/// Plaintext is never persisted on EntraInstanceRegistrations.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260827230000_EntraRegistrationClientSecretRef")]
public partial class EntraRegistrationClientSecretRef : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientSecretRef",
            table: "EntraInstanceRegistrations",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClientSecretRef",
            table: "EntraInstanceRegistrations");
    }
}
