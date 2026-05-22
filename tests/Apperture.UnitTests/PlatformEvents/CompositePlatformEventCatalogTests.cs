using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class CompositePlatformEventCatalogTests
{
    private sealed class Vm : IModule
    {
        public Vm(ModuleManifest m) => Manifest = m;
        public ModuleManifest Manifest { get; }
        public ModuleManifest GetManifest() => Manifest;
        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    [Fact]
    public void Resolves_builtin_schedule_types_and_module_signals()
    {
        var manifest = new ModuleManifest("Mod.Sample", "n", "1", "d")
        {
            Signals = [new ModuleSignalDeclaration("alarm", "A", "A", "critical")],
        };
        ModuleSignalManifestValidator.ThrowIfInvalid(new IModule[] { new Vm(manifest) });
        var registry = ModuleSignalRegistry.FromModules(new IModule[] { new Vm(manifest) });

        var sut = new CompositePlatformEventCatalog(registry);

        sut.IsKnownType(PlatformEventTypes.ScheduleExpectedRunMissing).Should().BeTrue();
        sut.IsKnownType(PlatformEventTypes.ExecutionRunCompleted).Should().BeTrue();
        sut.IsKnownType(PlatformEventTypes.ExecutionRunFailed).Should().BeTrue();
        sut.IsKnownType("apperture.module.mod.sample.alarm").Should().BeTrue();

        sut.GetRequired(PlatformEventTypes.ScheduleExpectedRunMissing).Type.Should().Be(PlatformEventTypes.ScheduleExpectedRunMissing);
        sut.GetRequired(PlatformEventTypes.ExecutionRunFailed).RequiresRunId.Should().BeTrue();
        sut.GetRequired("apperture.module.mod.sample.alarm").DefaultSource.Should().Be("/modules/mod.sample");

        sut.GetAll().Should().Contain(d => d.Type == PlatformEventTypes.ScheduleExpectedRunMissing);
        sut.GetAll().Should().Contain(d => d.Type == "apperture.module.mod.sample.alarm");
    }
}
