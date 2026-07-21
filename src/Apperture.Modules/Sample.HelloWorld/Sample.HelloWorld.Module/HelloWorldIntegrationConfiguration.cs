using Apperture.ModuleSdk.Integration.Configuration;
using Sample.HelloWorld.Contracts;

namespace Sample.HelloWorld;

internal static class HelloWorldIntegrationConfiguration
{
    internal static IReadOnlyList<IntegrationSettingDefinition> Settings { get; } =
    [
        EmitOnComplete(),
        GreetingPrefix(),
    ];

    internal static IReadOnlyList<ConnectionRequirementDefinition> Connections { get; } = [];

    internal static IReadOnlyList<ProcedureRequirementSet> Procedures { get; } = [];

    private static IntegrationSettingDefinition EmitOnComplete() =>
        new()
        {
            Key = HelloWorldDemoSettingKeys.EmitNotificationOnComplete,
            DisplayName = "Emit demo notification on complete",
            Description =
                "When enabled, the HelloWorld Complete step emits demo.completed so the built-in notification definition can fire.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = true,
            Order = 1,
            Group = "Demo",
        };

    private static IntegrationSettingDefinition GreetingPrefix() =>
        new()
        {
            Key = HelloWorldDemoSettingKeys.GreetingPrefix,
            DisplayName = "Greeting prefix",
            Description = "Prefix included in the demo.completed platform event message payload.",
            ValueKind = IntegrationSettingValueKind.String,
            IsRequired = false,
            DefaultValue = "Hello",
            Order = 2,
            Group = "Demo",
        };
}
