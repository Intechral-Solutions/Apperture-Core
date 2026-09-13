using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Adds the escalation kind and template variant to the built-in expected-run-missing definition.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260910160000_NtfC_ExpectedRunMissingEscalationKind")]
public partial class NtfC_ExpectedRunMissingEscalationKind : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        static string N(string value) => $"N'{value.Replace("'", "''")}'";

        var id = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        migrationBuilder.Sql($@"
UPDATE [NotificationDefinitions]
SET [RuleJson] = {N(ExpectedRunMissingNotificationDefinitionSeed.RuleJson)},
    [TemplateJson] = {N(ExpectedRunMissingNotificationDefinitionSeed.TemplateJson)},
    [UpdatedAtUtc] = SYSUTCDATETIME()
WHERE [Id] = {N(id)}
  AND [Origin] = N'{NotificationDefinitionOrigins.PlatformBuiltIn}';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-reversible: prior documents omitted the escalation kind. Restore from git history if needed.
    }
}
