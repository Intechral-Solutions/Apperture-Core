using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

/// <summary>
/// Mirrors Management Host wiring: merged catalog from known module manifests + static platform types (Epic 77 Phase 2B).
/// </summary>
public sealed class ManagementPlatformEventCatalogResolutionTests
{
    [Fact]
    public void Composite_resolves_platform_and_hello_world_module_signal_types()
    {
        IModule[] modules = [new HelloWorldModule()];
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var registry = ModuleSignalRegistry.FromModules(modules);
        var sut = new CompositePlatformEventCatalog(registry);

        sut.GetRequired(PlatformEventTypes.ScheduleExpectedRunMissing).Type.Should().Be(PlatformEventTypes.ScheduleExpectedRunMissing);
        sut.GetRequired(PlatformEventTypes.ScheduleExpectedRunCleared).Type.Should().Be(PlatformEventTypes.ScheduleExpectedRunCleared);

        var demoType = ModulePlatformEventTypes.Format("Sample.HelloWorld", "demo.completed");
        sut.GetRequired(demoType).Type.Should().Be(demoType);
        sut.GetRequired(demoType).DefaultSource.Should().Be("/modules/sample.helloworld");
    }

    [Fact]
    public void Unknown_type_throws_validation()
    {
        IModule[] modules = [new HelloWorldModule()];
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var sut = new CompositePlatformEventCatalog(ModuleSignalRegistry.FromModules(modules));

        var act = () => sut.GetRequired("apperture.module.none.nope");
        act.Should().Throw<PlatformEventValidationException>();
    }
}
