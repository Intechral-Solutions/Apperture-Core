using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Sample.HelloWorld;

namespace Apperture.UnitTests.Notifications;

internal static class NotificationValidationTestHelpers
{
    internal static CompositePlatformEventCatalog CreateCatalog()
    {
        IModule[] modules = [new HelloWorldModule()];
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        return new CompositePlatformEventCatalog(ModuleSignalRegistry.FromModules(modules));
    }

    internal static NotificationRuleValidator CreateRuleValidator() =>
        new(CreateCatalog(), new NotificationTemplateVariableCatalog(ModuleSignalRegistry.FromModules([new HelloWorldModule()])));

    internal static NotificationTemplateValidator CreateTemplateValidator() =>
        new(new NotificationTemplateVariableCatalog(ModuleSignalRegistry.FromModules([new HelloWorldModule()])), new NoOpRedactionPolicy());

    internal const string ValidUserRuleJson = """
        {
          "schemaVersion": "apperture.notification.rule/v1",
          "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
          "stages": [
            {
              "stage": "index",
              "filters": {
                "integrationId": { "operator": "equals", "value": "int-1" },
                "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
              }
            }
          ]
        }
        """;

    internal const string ValidUserTemplateJson = """
        {
          "schemaVersion": "apperture.notification.template/v1",
          "subject": "Alert {{integration.id}}",
          "summary": "Summary",
          "bodyText": "Status: {{event.data.evaluationStatus}}"
        }
        """;
}
