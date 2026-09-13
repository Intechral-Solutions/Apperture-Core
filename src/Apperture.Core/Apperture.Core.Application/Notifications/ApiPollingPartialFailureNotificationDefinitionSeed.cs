using Apperture.Core.Domain.Notifications;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.Notifications;

/// <summary>Built-in module notification for Sample.ApiPolling sync.completed.with.errors (SDK-5A).</summary>
public static class ApiPollingPartialFailureNotificationDefinitionSeed
{
    public static readonly string EventType =
        ModulePlatformEventTypes.Format("Sample.ApiPolling", "sync.completed.with.errors");

    public const string RuleJson = """
        {
          "schemaVersion": "apperture.notification.rule/v1",
          "trigger": {
            "eventTypes": [
              "apperture.module.sample.apipolling.sync.completed.with.errors"
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
                    "apperture.module.sample.apipolling.sync.completed.with.errors"
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
          "subject": "[Apperture] API poll completed with errors ({{integration.id}})",
          "summary": "API poll partial failure for {{integration.displayName}}",
          "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nSnapshot: {{event.data.snapshotId}}\nSync batch: {{event.data.syncBatchId}}\nSucceeded: {{event.data.succeededCount}}\nFailed: {{event.data.failedCount}}\nRun: {{run.id}}\n",
          "variants": {
            "activation": {
              "subject": "[Apperture] API poll completed with errors ({{integration.id}})",
              "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nSnapshot: {{event.data.snapshotId}}\nSync batch: {{event.data.syncBatchId}}\nSucceeded: {{event.data.succeededCount}}\nFailed: {{event.data.failedCount}}\nRun: {{run.id}}\n"
            }
          }
        }
        """;

    public static NotificationDefinition BuiltIn(DateTimeOffset seededAtUtc) =>
        new(
            NotificationDefinitionId.From(NotificationDefinitionIds.ModuleSampleApiPollingSyncCompletedWithErrors),
            "API poll completed with errors",
            "Sample module built-in: fires when ApiPolling emits sync.completed.with.errors.",
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.ModuleBuiltIn,
            NotificationDefinitionScopes.Module,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            ModuleId: "Sample.ApiPolling",
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
