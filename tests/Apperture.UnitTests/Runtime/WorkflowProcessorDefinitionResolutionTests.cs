using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using System.Reflection;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class WorkflowProcessorDefinitionResolutionTests
{
    [Fact]
    public async Task ResolveDefinitionOrThrowAsync_WhenCatalogShowsModuleNotEffective_ThrowsDefinitionNotExecutable()
    {
        await using var db = CreateDb();
        var module = new HelloWorldModule();
        var registry = new StepHandlerRegistry();
        module.RegisterStepHandlers(registry);

        var executor = new DefaultWorkflowExecutor(new NoOpSlotResolver(), registry);
        var definitionResolver = new WorkflowDefinitionResolver([module], new DenyAllActivationFilter());

        IModule mod = module;
        var descriptor = await mod.BuildDescriptorAsync(CancellationToken.None);
        var catalogEntry = new ModuleCatalogEntry(
            descriptor,
            ModuleLifecycleState.InstalledDisabled,
            Installed: true,
            Enabled: false,
            EffectivelyActive: false,
            ValidationIssues: []);

        var stubRegistry = new StubModuleRegistry([catalogEntry]);
        var processor = new WorkflowStartRequestProcessor(
            db,
            executor,
            definitionResolver,
            "worker",
            Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            moduleRegistry: stubRegistry);

        var act = async () => await InvokeResolve(processor, "Sample.HelloWorld.HelloWorld");

        await act.Should().ThrowAsync<DefinitionNotExecutableException>()
            .Where(x => x.Message.Contains("not effectively active", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task InvokeResolve(WorkflowStartRequestProcessor processor, string workflowDefinitionId)
    {
        var method = typeof(WorkflowStartRequestProcessor).GetMethod(
            "ResolveDefinitionOrThrowAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(string), typeof(CancellationToken)],
            null);
        if (method is null)
            throw new InvalidOperationException("ResolveDefinitionOrThrowAsync not found");
        var result = method.Invoke(processor, new object[] { workflowDefinitionId, CancellationToken.None });
        if (result is ValueTask<WorkflowDefinition> vt)
            await vt.ConfigureAwait(false);
        else
            throw new InvalidOperationException($"Unexpected return type {result?.GetType().FullName}");
    }

    private static AppertureDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppertureDbContext(options);
    }

    private sealed class NoOpSlotResolver : IRuntimeSlotResolver
    {
        public ValueTask<T?> ResolveSlotAsync<T>(string moduleId, string slotName, CancellationToken cancellationToken = default)
            where T : class =>
            ValueTask.FromResult<T?>(null);
    }

    private sealed class DenyAllActivationFilter : IModuleActivationFilter
    {
        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class StubModuleRegistry : IModuleRegistryService
    {
        private readonly IReadOnlyList<ModuleCatalogEntry> _catalog;

        public StubModuleRegistry(IReadOnlyList<ModuleCatalogEntry> catalog) => _catalog = catalog;

        public ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_catalog);

        public ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_catalog.FirstOrDefault(x => x.Descriptor.ModuleId == moduleId));

        public ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ModuleDescriptor>>([]);

        public ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
