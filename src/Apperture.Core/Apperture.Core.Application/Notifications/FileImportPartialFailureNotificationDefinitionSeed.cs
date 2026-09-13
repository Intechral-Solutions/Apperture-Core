using Apperture.Core.Domain.Notifications;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.Notifications;

/// <summary>Built-in module notification for Sample.FileImport import.completed.with.errors (SDK-2).</summary>
public static class FileImportPartialFailureNotificationDefinitionSeed
{
    public static readonly string EventType =
        ModulePlatformEventTypes.Format("Sample.FileImport", "import.completed.with.errors");

    public const string RuleJson = """
        {
          "schemaVersion": "apperture.notification.rule/v1",
          "trigger": {
            "eventTypes": [
              "apperture.module.sample.fileimport.import.completed.with.errors"
            ],
            "notificationKinds": ["activation"]
          },
          "stages": [
            {
              "stage": "index",
              "filters": {
                "type": {
                  "operator": "in",
                  "values": [
                    "apperture.module.sample.fileimport.import.completed.with.errors"
                  ]
                }
              }
            }
          ],
          "throttle": { "perSubjectMinInterval": "0m" },
          "requiresIncident": false,
          "priority": 0
        }
        """;

    public const string TemplateJson = """
        {
          "schemaVersion": "apperture.notification.template/v1",
          "subject": "[Apperture] File import completed with errors ({{integration.id}})",
          "summary": "File import partial failure for {{integration.displayName}}",
          "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nFixture: {{event.data.fixtureId}}\nBatch: {{event.data.batchId}}\nSucceeded: {{event.data.succeededCount}}\nFailed: {{event.data.failedCount}}\nRun: {{run.id}}\n",
          "variants": {
            "activation": {
              "subject": "[Apperture] File import completed with errors ({{integration.id}})",
              "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nFixture: {{event.data.fixtureId}}\nBatch: {{event.data.batchId}}\nSucceeded: {{event.data.succeededCount}}\nFailed: {{event.data.failedCount}}\nRun: {{run.id}}\n"
            }
          }
        }
        """;

    public static NotificationDefinition BuiltIn(DateTimeOffset seededAtUtc) =>
        new(
            NotificationDefinitionId.From(NotificationDefinitionIds.ModuleSampleFileImportCompletedWithErrors),
            "File import completed with errors",
            "Sample module built-in: fires when FileImport emits import.completed.with.errors.",
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.ModuleBuiltIn,
            NotificationDefinitionScopes.Module,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            ModuleId: "Sample.FileImport",
            IntegrationId: null,
            PrimaryEventType: EventType,
            IsEnabled: true,
            DefaultSeverity: "warning",
            RuleJson,
            TemplateJson,
            seededAtUtc,
            seededAtUtc,
            CreatedByActorId: null,
            UpdatedByActorId: null);
}
