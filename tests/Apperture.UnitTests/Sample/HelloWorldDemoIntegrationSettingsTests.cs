using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Sample.HelloWorld;
using Sample.HelloWorld.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample;

public sealed class HelloWorldDemoIntegrationSettingsTests
{
    [Fact]
    public void HelloWorldModule_contributes_integration_scoped_demo_settings()
    {
        var module = new HelloWorldModule();
        module.Should().BeAssignableTo<IModuleIntegrationSettingDefinitionContributor>();

        var defs = module.GetIntegrationSettingDefinitions();
        defs.Should().Contain(d => d.Key == HelloWorldDemoSettingKeys.EmitNotificationOnComplete);
        defs.Should().Contain(d => d.Key == HelloWorldDemoSettingKeys.GreetingPrefix);

        var emit = defs.Single(d => d.Key == HelloWorldDemoSettingKeys.EmitNotificationOnComplete);
        emit.AllowedValueScopes.Should().Contain(PlatformSettingValueScopeKind.Integration);
        emit.DefaultValue.Should().Be(true);
    }
}
