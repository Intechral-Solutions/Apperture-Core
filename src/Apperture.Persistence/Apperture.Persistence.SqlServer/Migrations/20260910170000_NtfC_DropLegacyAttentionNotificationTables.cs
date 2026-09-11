using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Drops the unused Attention delivery ledger and cutover sign-off tables after the platform pipeline became the only path.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260910170000_NtfC_DropLegacyAttentionNotificationTables")]
public partial class NtfC_DropLegacyAttentionNotificationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AttentionIncidentNotifications");
        migrationBuilder.DropTable(name: "ExpectedRunMissingCutoverSignOffs");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-reversible: historical create migrations remain if a restore from backup is required.
    }
}
