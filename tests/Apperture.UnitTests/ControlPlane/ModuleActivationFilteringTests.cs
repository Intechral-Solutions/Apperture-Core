using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ModuleActivationFilteringTests
{
    [Fact]
    public async Task WorkflowDefinitionResolver_excludes_disabled_modules()
    {
        var resolver = new WorkflowDefinitionResolver(
            [new TestModule("Enabled.Module"), new TestModule("Disabled.Module")],
            new StubActivationFilter(["Enabled.Module"]));

        var enabled = await resolver.GetByIdAsync(new WorkflowDefinitionId("Enabled.Module.Workflow"));
        var disabled = await resolver.GetByIdAsync(new WorkflowDefinitionId("Disabled.Module.Workflow"));

        enabled.Should().NotBeNull();
        disabled.Should().BeNull();
    }

    [Fact]
    public async Task OperationRegistry_excludes_disabled_modules()
    {
        var registry = new OperationRegistryService(
            [new TestModule("Enabled.Module"), new TestModule("Disabled.Module")],
            new StubActivationFilter(["Enabled.Module"]));

        var list = await registry.ListAsync();

        list.Should().ContainSingle(x => x.Id.Value == "Enabled.Module.Operation");
        list.Should().NotContain(x => x.Id.Value == "Disabled.Module.Operation");
    }

    private sealed class StubActivationFilter : IModuleActivationFilter
    {
        private readonly HashSet<string> _enabled;

        public StubActivationFilter(IEnumerable<string> enabled)
        {
            _enabled = enabled.ToHashSet(StringComparer.Ordinal);
        }

        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_enabled.Contains(moduleId));
    }

    private sealed class TestModule : IModule, IOperationDefinitionProvider
    {
        private readonly string _id;

        public TestModule(string id)
        {
            _id = id;
        }

        public ModuleManifest GetManifest() => new(_id, _id, "1.0.0", "test");

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(new WorkflowDefinitionId($"{_id}.Workflow"), "Test", _id, ["Step1"]);
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OperationDefinition(new OperationId($"{_id}.Operation"), "TestOperation", _id, OperationCategory.Command, null, []);
            await Task.CompletedTask;
        }
    }
}
