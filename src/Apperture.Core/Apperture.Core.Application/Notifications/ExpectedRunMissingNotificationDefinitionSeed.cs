using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

/// <summary>Built-in platform definition seed (Epic 69 parity; not wired to orchestrator).</summary>
public static class ExpectedRunMissingNotificationDefinitionSeed
{
    public const string RuleJson = """
        {
          "schemaVersion": "apperture.notification.rule/v1",
          "trigger": {
            "eventTypes": [
              "apperture.schedule.expected_run.missing",
              "apperture.schedule.expected_run.cleared"
            ],
            "incidentTransitions": ["opened", "observedStillActive", "resolved"],
            "notificationKinds": ["activation", "reminder", "resolved"]
          },
          "stages": [
            {
              "stage": "index",
              "filters": {
                "type": {
                  "operator": "in",
                  "values": [
                    "apperture.schedule.expected_run.missing",
                    "apperture.schedule.expected_run.cleared"
                  ]
                },
                "integrationId": { "operator": "present" }
              }
            }
          ],
          "throttle": { "perSubjectMinInterval": "0m" },
          "requiresIncident": true,
          "priority": 0
        }
        """;

    public const string TemplateJson = """
        {
          "schemaVersion": "apperture.notification.template/v1",
          "subject": "[Apperture] Attention: expected_run_missing ({{integration.id}})",
          "summary": "Expected run missing for {{integration.displayName}}",
          "bodyText": "Integration: {{integration.displayName}} ({{integration.id}})\nCondition: expected_run_missing\nEvaluation status: {{event.data.evaluationStatus}}\nMost recent expected fire (UTC): {{event.data.mostRecentExpectedFireUtc}}\nOperator summary: {{event.data.operatorSummaryLine}}\n",
          "actionText": "Review schedule diagnostics",
          "actionUrl": "/integrations/{{integration.id}}/schedule-diagnostics",
          "variants": {
            "activation": {
              "subject": "[Apperture] Attention: expected_run_missing ({{integration.id}})",
              "bodyText": "Integration: {{integration.displayName}} ({{integration.id}})\nCondition: expected_run_missing\nEvaluation status: {{event.data.evaluationStatus}}\nMost recent expected fire (UTC): {{event.data.mostRecentExpectedFireUtc}}\nOperator summary: {{event.data.operatorSummaryLine}}\n",
              "actionText": "Review schedule diagnostics",
              "actionUrl": "/integrations/{{integration.id}}/schedule-diagnostics"
            },
            "reminder": {
              "subject": "[Apperture] Reminder: expected_run_missing ({{integration.id}})",
              "bodyText": "This incident is still active.\n\nIntegration: {{integration.displayName}} ({{integration.id}})\nCondition: expected_run_missing\nEvaluation status: {{event.data.evaluationStatus}}\nMost recent expected fire (UTC): {{event.data.mostRecentExpectedFireUtc}}\nOperator summary: {{event.data.operatorSummaryLine}}\n",
              "actionText": "Review schedule diagnostics",
              "actionUrl": "/integrations/{{integration.id}}/schedule-diagnostics"
            },
            "resolved": {
              "subject": "[Apperture] Resolved: expected_run_missing ({{integration.id}})",
              "bodyText": "The expected_run_missing attention incident for integration {{integration.id}} has resolved (expectation cleared)."
            }
          }
        }
        """;

    public static NotificationDefinition BuiltIn(DateTimeOffset seededAtUtc) =>
        new(
            NotificationDefinitionId.From(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing),
            "Expected run missing",
            "Platform built-in for schedule expectation attention.",
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.PlatformBuiltIn,
            NotificationDefinitionScopes.Platform,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            ModuleId: null,
            IntegrationId: null,
            PrimaryEventType: PlatformEventTypes.ScheduleExpectedRunMissing,
            IsEnabled: true,
            DefaultSeverity: "warning",
            RuleJson,
            TemplateJson,
            seededAtUtc,
            seededAtUtc,
            CreatedByActorId: null,
            UpdatedByActorId: null);
}
