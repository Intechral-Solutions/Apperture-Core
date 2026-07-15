using System;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260715120000_Sdk2_FileImportPartialFailureNotificationDefinition")]
public partial class Sdk2_FileImportPartialFailureNotificationDefinition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var seededAt = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var seed = FileImportPartialFailureNotificationDefinitionSeed.BuiltIn(seededAt);

        static string N(object value) =>
            value is null ? "NULL" : $"N'{((string)value).Replace("'", "''")}'";

        static string DT(DateTimeOffset value) =>
            $"'{value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffZ}'";

        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM [NotificationDefinitions] WHERE [Id] = {N(seed.Id.Value)})
BEGIN
    INSERT INTO [NotificationDefinitions]
        ([Id],[DisplayName],[Description],[SourceKind],[Origin],[Scope],[Mutability],
         [ModuleId],[IntegrationId],[PrimaryEventType],[IsEnabled],[DefaultSeverity],
         [RuleJson],[TemplateJson],[CreatedAtUtc],[UpdatedAtUtc],[CreatedByActorId],[UpdatedByActorId])
    VALUES
        ({N(seed.Id.Value)},{N(seed.DisplayName)},{N(seed.Description)},{N(seed.SourceKind)},{N(seed.Origin)},
         {N(seed.Scope)},{N(seed.Mutability)},{N(seed.ModuleId)},{N(seed.IntegrationId)},
         {N(seed.PrimaryEventType)},{(seed.IsEnabled ? 1 : 0)},{N(seed.DefaultSeverity)},
         {N(seed.RuleJson)},{N(seed.TemplateJson)},{DT(seed.CreatedAtUtc)},{DT(seed.UpdatedAtUtc)},
         {N(seed.CreatedByActorId)},{N(seed.UpdatedByActorId)});
END";

        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        static string N(object value) => $"N'{((string)value).Replace("'", "''")}'";

        var id = NotificationDefinitionIds.ModuleSampleFileImportCompletedWithErrors;
        migrationBuilder.Sql($@"
DELETE FROM [NotificationDefinitions]
WHERE [Id] = {N(id)};");
    }
}
