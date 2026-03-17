using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class WorkflowStartAuthorizationServiceTests
{
    private static readonly WorkflowDefinitionId WorkflowId = new("Sample.HelloWorld.HelloWorld");

    [Fact]
    public async Task EnsureCanStartWorkflowAsync_WhenDefinitionHasNoRequiredPermissions_UsesFallback_ModuleOwned()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: null));
        var registry = new StubRegistry(known: new[]
        {
            // fallback permission must exist for our definition-level evaluator to not matter; registry used only by required perms path
            new PermissionDescriptor("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Module("Sample.HelloWorld")))
        });
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowStartAuthorizationService(principal, auth, workflowModuleResolver, workflowDefinitionResolver, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartWorkflowAsync(WorkflowId);
        await act.Should().NotThrowAsync();
        auth.LastPermission!.Action.Should().Be("workflow.execute");
        auth.LastResource!.ScopeKey.Should().Be(ScopeKeyConstants.Module("Sample.HelloWorld"));
    }

    [Fact]
    public async Task EnsureCanStartWorkflowAsync_WhenDefinitionHasOneRequiredPermission_EvaluatesThatPermission()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var required = new PermissionDescriptor("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Module("Sample.HelloWorld")));
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: new[] { required }));
        var registry = new StubRegistry(known: new[] { required });
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowStartAuthorizationService(principal, auth, workflowModuleResolver, workflowDefinitionResolver, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartWorkflowAsync(WorkflowId);
        await act.Should().NotThrowAsync();
        auth.LastPermission.Should().Be(required);
        auth.LastResource.Should().Be(required.Resource);
    }

    [Fact]
    public async Task EnsureCanStartWorkflowAsync_WhenDefinitionHasMultipleRequiredPermissions_RequiresAll()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true, denyAfter: 1);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var p1 = new PermissionDescriptor("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Module("Sample.HelloWorld")));
        var p2 = new PermissionDescriptor(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance));
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: new[] { p1, p2 }));
        var registry = new StubRegistry(known: new[] { p1, p2 });
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowStartAuthorizationService(principal, auth, workflowModuleResolver, workflowDefinitionResolver, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartWorkflowAsync(WorkflowId);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task EnsureCanStartWorkflowAsync_WhenRequiredPermissionIsUnknownInRegistry_Denies()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var workflowModuleResolver = new StubWorkflowModuleResolver(moduleId: "Sample.HelloWorld");
        var unknown = new PermissionDescriptor("unknown.permission", new ResourceDescriptor("WorkflowInstance", ScopeKeyConstants.Module("Sample.HelloWorld")));
        var workflowDefinitionResolver = new StubWorkflowDefinitionResolver(definition: new WorkflowDefinition(
            WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "s1" }, WorkflowCategory.Command, RequiredPermissions: new[] { unknown }));
        var registry = new StubRegistry(known: Array.Empty<PermissionDescriptor>());
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);

        var svc = new WorkflowStartAuthorizationService(principal, auth, workflowModuleResolver, workflowDefinitionResolver, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartWorkflowAsync(WorkflowId);
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
        private readonly int? _denyAfter;
        private int _count;

        public RecordingAuthorizationService(bool allowed, int? denyAfter = null)
        {
            _allowed = allowed;
            _denyAfter = denyAfter;
        }

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
            if (_denyAfter is not null && _count > _denyAfter.Value)
                return ValueTask.FromResult(false);
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
    }
}

