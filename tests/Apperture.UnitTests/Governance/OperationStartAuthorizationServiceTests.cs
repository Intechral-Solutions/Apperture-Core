using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class OperationStartAuthorizationServiceTests
{
    private static readonly OperationId OperationId = new("Sample.HelloWorld.DoWork");

    [Fact]
    public async Task EnsureCanStartOperationAsync_WhenDefinitionHasNoRequiredPermissions_UsesFallback_ModuleOwned()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true);
        var registry = new StubRegistry(Array.Empty<PermissionDescriptor>());
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);
        var operations = new StubOperationRegistry(new OperationDefinition(
            OperationId,
            "DoWork",
            "Sample.HelloWorld",
            OperationCategory.Command,
            ParameterSchema: null,
            RequiredPermissions: Array.Empty<PermissionDescriptor>()));

        var svc = new OperationStartAuthorizationService(principal, auth, operations, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartOperationAsync(OperationId);
        await act.Should().NotThrowAsync();
        auth.LastResource!.ScopeKey.Should().Be("module:Sample.HelloWorld");
    }

    [Fact]
    public async Task EnsureCanStartOperationAsync_WhenDefinitionHasRequiredPermissions_RequiresAll()
    {
        var principal = new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
        var auth = new RecordingAuthorizationService(allowed: true, denyAfter: 1);
        var required = new[]
        {
            new PermissionDescriptor("operation.execute", new ResourceDescriptor("OperationDefinition", "module:Sample.HelloWorld")),
            new PermissionDescriptor("operation.read", new ResourceDescriptor("OperationDefinition", "module:Sample.HelloWorld"))
        };
        var registry = new StubRegistry(required);
        var execEvaluator = new ExecutablePermissionEvaluator(auth, registry);
        var operations = new StubOperationRegistry(new OperationDefinition(
            OperationId,
            "DoWork",
            "Sample.HelloWorld",
            OperationCategory.Command,
            ParameterSchema: null,
            RequiredPermissions: required));

        var svc = new OperationStartAuthorizationService(principal, auth, operations, execEvaluator);

        Func<Task> act = async () => await svc.EnsureCanStartOperationAsync(OperationId);
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

    private sealed class StubRegistry : IResourceDescriptorRegistry
    {
        private readonly IReadOnlyList<PermissionDescriptor> _known;
        public StubRegistry(IReadOnlyList<PermissionDescriptor> known) => _known = known;
        public void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null) => throw new NotSupportedException();
        public void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null) => throw new NotSupportedException();
        public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) => Array.Empty<ResourceDescriptor>();
        public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) => _known;
    }

    private sealed class StubOperationRegistry : IOperationRegistryService
    {
        private readonly OperationDefinition _definition;
        public StubOperationRegistry(OperationDefinition definition) => _definition = definition;
        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(_definition);
        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(new[] { _definition });
        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

