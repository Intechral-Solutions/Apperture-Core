using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260702120000_S8B_ExpectedRunMissingTemplateActionUrl")]
public partial class S8B_ExpectedRunMissingTemplateActionUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        static string N(string value) => $"N'{value.Replace("'", "''")}'";

        var templateJson = ExpectedRunMissingNotificationDefinitionSeed.TemplateJson;
        var id = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;

        migrationBuilder.Sql($@"
UPDATE [NotificationDefinitions]
SET [TemplateJson] = {N(templateJson)},
    [UpdatedAtUtc] = SYSUTCDATETIME()
WHERE [Id] = {N(id)}
  AND [Origin] = N'{NotificationDefinitionOrigins.PlatformBuiltIn}';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-reversible: prior template lacked actionUrl; re-seed from git history if rollback required.
    }
}
