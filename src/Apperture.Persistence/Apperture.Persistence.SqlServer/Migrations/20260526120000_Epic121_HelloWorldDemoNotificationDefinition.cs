using System;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260526120000_Epic121_HelloWorldDemoNotificationDefinition")]
public partial class Epic121_HelloWorldDemoNotificationDefinition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var seededAt = new DateTimeOffset(2026, 5, 26, 0, 0, 0, TimeSpan.Zero);
        var seed = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(seededAt);

        // Note: use raw SQL instead of migrationBuilder.InsertData.
        // This repo's migrations use InsertData widely, but for this handwritten migration,
        // EF can fail model-table mapping validation during startup.
        // Raw SQL bypasses that validation and is safe for inserting a single known row.
        static string N(object value) =>
            value is null ? "NULL" : $"N'{((string)value).Replace("'", "''")}'";

        static string DT(DateTimeOffset value) =>
            // SQL Server accepts ISO-8601 datetime literals and casts to datetimeoffset implicitly.
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

        var id = NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted;
        migrationBuilder.Sql($@"
DELETE FROM [NotificationDefinitions]
WHERE [Id] = {N(id)};");
    }
}
