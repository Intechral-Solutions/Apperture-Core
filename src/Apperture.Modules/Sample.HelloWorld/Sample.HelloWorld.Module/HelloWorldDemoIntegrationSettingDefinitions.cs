using Apperture.Core.Application.PlatformSettings;
using Sample.HelloWorld.Contracts;

namespace Sample.HelloWorld;

internal static class HelloWorldDemoIntegrationSettingDefinitions
{
    private static readonly PlatformSettingValueScopeKind[] IntegrationScopes =
        [PlatformSettingValueScopeKind.Integration];

    internal static IReadOnlyList<PlatformSettingDefinition> All { get; } =
    [
        EmitOnComplete(),
        GreetingPrefix()
    ];

    private static PlatformSettingDefinition EmitOnComplete() =>
        new()
        {
            Key = HelloWorldDemoSettingKeys.EmitNotificationOnComplete,
            DisplayName = "Emit demo notification on complete",
            Description =
                "When enabled, the HelloWorld Complete step emits demo.completed so the built-in notification definition can fire.",
            Category = PlatformSettingCategory.Notifications,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.HelloWorld:Demo:EmitNotificationOnComplete",
            DefaultValue = true,
            RequiresRestart = false
        };

    private static PlatformSettingDefinition GreetingPrefix() =>
        new()
        {
            Key = HelloWorldDemoSettingKeys.GreetingPrefix,
            DisplayName = "Greeting prefix",
            Description = "Prefix included in the demo.completed platform event message payload.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.String,
            InputKind = PlatformSettingInputKind.Text,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.HelloWorld:Demo:GreetingPrefix",
            DefaultValue = "Hello",
            RequiresRestart = false
        };
}
