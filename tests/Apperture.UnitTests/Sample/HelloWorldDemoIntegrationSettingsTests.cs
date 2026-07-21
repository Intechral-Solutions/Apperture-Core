using Apperture.ModuleSdk.Integration.Configuration;
using FluentAssertions;
using Sample.HelloWorld;
using Sample.HelloWorld.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample;

public sealed class HelloWorldDemoIntegrationSettingsTests
{
    [Fact]
    public void HelloWorldModule_contributes_optional_integration_settings()
    {
        var module = new HelloWorldModule();
        module.Should().BeAssignableTo<IIntegrationConfigurationContributor>();

        var defs = module.GetSettingDefinitions();
        defs.Should().Contain(d => d.Key == HelloWorldDemoSettingKeys.EmitNotificationOnComplete);
        defs.Should().Contain(d => d.Key == HelloWorldDemoSettingKeys.GreetingPrefix);

        defs.Should().OnlyContain(d => !d.IsRequired);

        var emit = defs.Single(d => d.Key == HelloWorldDemoSettingKeys.EmitNotificationOnComplete);
        emit.DefaultValue.Should().Be(true);
    }

    [Fact]
    public void HelloWorldModule_has_no_required_connections()
    {
        var module = new HelloWorldModule();
        module.GetConnectionRequirements().Should().BeEmpty();
    }
}
