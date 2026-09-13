using Apperture.Core.Domain.Notifications;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.Notifications;

/// <summary>Built-in module notification for Sample.HelloWorld demo.completed (Epic 121 sample).</summary>
public static class HelloWorldDemoNotificationDefinitionSeed
{
    public static readonly string EventType = ModulePlatformEventTypes.Format("Sample.HelloWorld", "demo.completed");

    public const string RuleJson = """
        {
          "schemaVersion": "apperture.notification.rule/v1",
          "trigger": {
            "eventTypes": [
              "apperture.module.sample.helloworld.demo.completed"
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
                    "apperture.module.sample.helloworld.demo.completed"
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
          "subject": "[Apperture] HelloWorld demo completed ({{integration.id}})",
          "summary": "HelloWorld workflow completed for {{integration.displayName}}",
          "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nMessage: {{event.data.message}}\nRun: {{run.id}}\n",
          "variants": {
            "activation": {
              "subject": "[Apperture] HelloWorld demo completed ({{integration.id}})",
              "bodyText": "Integration: {{integration.id}}\nModule: {{module.displayName}}\nMessage: {{event.data.message}}\nRun: {{run.id}}\n"
            }
          }
        }
        """;

    public static NotificationDefinition BuiltIn(DateTimeOffset seededAtUtc) =>
        new(
            NotificationDefinitionId.From(NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted),
            "HelloWorld demo completed",
            "Sample module built-in: fires when HelloWorld emits demo.completed.",
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.ModuleBuiltIn,
            NotificationDefinitionScopes.Module,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            ModuleId: "Sample.HelloWorld",
            IntegrationId: null,
            PrimaryEventType: EventType,
            IsEnabled: true,
            DefaultSeverity: "info",
            RuleJson,
            TemplateJson,
            seededAtUtc,
            seededAtUtc,
            CreatedByActorId: null,
            UpdatedByActorId: null);
}
