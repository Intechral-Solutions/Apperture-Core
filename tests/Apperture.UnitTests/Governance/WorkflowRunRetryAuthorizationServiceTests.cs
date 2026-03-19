using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class WorkflowRunRetryAuthorizationServiceTests
{
    private static readonly WorkflowDefinitionId WorkflowId = new("Sample.HelloWorld.HelloWorld");

    [Fact]
    public async Task EnsureCanRetryRunAsync_WhenDefinitionHasNoRequiredPermissions_UsesFallback_ModuleOwned()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: null));
        var registry = new StubRegistry(known: new[]
        {
            new PermissionDescriptor("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Module("Sample.HelloWorld")))
        });
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowRunRetryAuthorizationService(principal, auth, workflowDefinitionResolver, execEvaluator, workflowModuleResolver);

        Func<Task> act = async () => await svc.EnsureCanRetryRunAsync(WorkflowId);
        await act.Should().NotThrowAsync();
        auth.LastPermission!.Action.Should().Be("workflow.execute");
        auth.LastResource!.ScopeKey.Should().Be(ScopeKeyConstants.Module("Sample.HelloWorld"));
    }

    [Fact]
    public async Task EnsureCanRetryRunAsync_WhenRequiredPermissionIsUnknownInRegistry_Denies()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var unknown = new PermissionDescriptor("unknown.permission", new ResourceDescriptor("WorkflowInstance", ScopeKeyConstants.Module("Sample.HelloWorld")));
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: new[] { unknown }));
        var registry = new StubRegistry(known: Array.Empty<PermissionDescriptor>());
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowRunRetryAuthorizationService(principal, auth, workflowDefinitionResolver, execEvaluator, workflowModuleResolver);

        Func<Task> act = async () => await svc.EnsureCanRetryRunAsync(WorkflowId);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;
        public StubPrincipalContext(IActor? actor) => _actor = actor;
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_actor);
    }

    private sealed class RecordingAuthorizationService : IAuthorizationService
    {
        private readonly bool _allowed;
        private int _count;

        public RecordingAuthorizationService(bool allowed) => _allowed = allowed;
        public PermissionDescriptor? LastPermission { get; private set; }
        public ResourceDescriptor? LastResource { get; private set; }

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default)
        {
            _count++;
            LastPermission = permission;
            LastResource = resource;
            return ValueTask.FromResult(_allowed);
        }
    }

    private sealed class StubWorkflowModuleResolver : IWorkflowModuleResolver
    {
        private readonly string? _moduleId;
        public StubWorkflowModuleResolver(string? moduleId) => _moduleId = moduleId;
        public ValueTask<string?> GetModuleIdForWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_moduleId);
    }

    private sealed class StubWorkflowDefinitionResolver : IWorkflowDefinitionResolver
    {
        private readonly WorkflowDefinition? _definition;
        public StubWorkflowDefinitionResolver(WorkflowDefinition? definition) => _definition = definition;
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_definition);
    }

    private sealed class StubRegistry : IResourceDescriptorRegistry
    {
        private readonly IReadOnlyList<PermissionDescriptor> _known;
        public StubRegistry(IReadOnlyList<PermissionDescriptor> known) => _known = known;

        public void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null) => throw new NotSupportedException();
        public void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null) => throw new NotSupportedException();
        public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) => Array.Empty<ResourceDescriptor>();
        public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) => _known;
        public bool IsPermissionRegistered(PermissionDescriptor descriptor) => _known.Any(p => p.Equals(descriptor));
    }
}

