using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RuntimeModuleCompositionQueryServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_DistinguishesEffectivelyExecutable()
    {
        var descriptor = new ModuleDescriptor(
            ModuleId: "m1",
            DisplayName: "M1",
            Version: "1.0",
            Description: null,
            Publisher: null,
            MinimumPlatformVersion: null,
            Contributions: [new WorkflowContributionDescriptor("w1", "W")]);

        var descriptor2 = descriptor with { ModuleId = "m2", DisplayName = "M2" };

        var catalog = new List<ModuleCatalogEntry>
        {
            new(descriptor, ModuleLifecycleState.InstalledEnabled, Installed: true, Enabled: true, EffectivelyActive: true, ValidationIssues: []),
            new(descriptor2, ModuleLifecycleState.InstalledDisabled, Installed: true, Enabled: false, EffectivelyActive: false, ValidationIssues: [])
        };

        var registry = new StubModuleRegistry(catalog, enabledDescriptors: [descriptor]);
        var svc = new RuntimeModuleCompositionQueryService(registry);

        var snap = await svc.GetSnapshotAsync();

        snap.EffectivelyExecutableModuleIds.Should().Equal("m1");
        snap.Modules.Should().HaveCount(2);
        snap.Modules.Single(x => x.ModuleId == "m1").EffectivelyExecutable.Should().BeTrue();
        snap.Modules.Single(x => x.ModuleId == "m2").EffectivelyExecutable.Should().BeFalse();
        snap.RestartBoundExecutionSurfaces.Should().Contain(nameof(RestartBoundSurface.StepHandlerRegistry));
        snap.ImmediatelyGovernedExecutionSurfaces.Should().Contain("WorkflowDefinitionResolver");
    }

    private sealed class StubModuleRegistry : IModuleRegistryService
    {
        private readonly IReadOnlyList<ModuleCatalogEntry> _catalog;
        private readonly IReadOnlyList<ModuleDescriptor> _enabled;

        public StubModuleRegistry(IReadOnlyList<ModuleCatalogEntry> catalog, IReadOnlyList<ModuleDescriptor> enabledDescriptors)
        {
            _catalog = catalog;
            _enabled = enabledDescriptors;
        }

        public ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_catalog);

        public ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_catalog.FirstOrDefault(x => x.Descriptor.ModuleId == moduleId));

        public ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_enabled);

        public ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
