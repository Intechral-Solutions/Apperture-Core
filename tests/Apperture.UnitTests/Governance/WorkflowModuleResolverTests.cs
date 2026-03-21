using Apperture.ControlPlane.Security;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class WorkflowModuleResolverTests
{
    private static readonly WorkflowDefinitionId HelloWorldId = new("Sample.HelloWorld.HelloWorld");

    [Fact]
    public async Task GetModuleIdForWorkflowAsync_WhenModuleEnabled_ReturnsModuleId()
    {
        var module = new HelloWorldModule();
        var filter = new StubActivationFilter(enabled: true);
        var resolver = new WorkflowModuleResolver([module], filter);

        var id = await resolver.GetModuleIdForWorkflowAsync(HelloWorldId);

        id.Should().Be("Sample.HelloWorld");
    }

    [Fact]
    public async Task GetModuleIdForWorkflowAsync_WhenModuleDisabled_ReturnsNull()
    {
        var module = new HelloWorldModule();
        var filter = new StubActivationFilter(enabled: false);
        var resolver = new WorkflowModuleResolver([module], filter);

        var id = await resolver.GetModuleIdForWorkflowAsync(HelloWorldId);

        id.Should().BeNull();
    }

    private sealed class StubActivationFilter : IModuleActivationFilter
    {
        private readonly bool _enabled;
        public StubActivationFilter(bool enabled) => _enabled = enabled;

        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_enabled);
    }
}
