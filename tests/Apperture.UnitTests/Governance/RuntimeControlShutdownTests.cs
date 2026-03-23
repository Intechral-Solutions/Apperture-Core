using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Security.Trust;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class RuntimeControlShutdownTests
{
    [Fact]
    public async Task Controller_WhenNoActor_Returns401()
    {
        var controller = new RuntimeControlsController(
            control: new StubRuntimeControlService(),
            runtimeIdentity: new StubRuntimeIdentityService(runtimeIdentity: null),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true, evaluateCallCountHolder: new EvaluateCallCountHolder()),
            principalContext: new StubPrincipalContext(actor: null),
            environment: new StubEnvironmentContext("local"));

        var result = await controller.RequestShutdown("rt-1", CancellationToken.None);
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Controller_WhenNotAuthorized_Returns403_AndDoesNotResolveRuntimeOrPersist()
    {
        var evaluateCalls = new EvaluateCallCountHolder();
        var control = new StubRuntimeControlService();
        var runtime = new StubRuntimeIdentityService(runtimeIdentity: new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow));

        var controller = new RuntimeControlsController(
            control: control,
            runtimeIdentity: runtime,
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: false, evaluateCallCountHolder: evaluateCalls),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            environment: new StubEnvironmentContext("local"));

        var result = await controller.RequestShutdown("rt-1", CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        evaluateCalls.Value.Should().Be(1);
        runtime.GetByIdCallCount.Should().Be(0);
        control.RequestShutdownCalls.Should().Be(0);
    }

    [Fact]
    public async Task Controller_WhenRuntimeMissing_Returns404()
    {
        var control = new StubRuntimeControlService();
        var runtime = new StubRuntimeIdentityService(runtimeIdentity: null);
        var evaluateCalls = new EvaluateCallCountHolder();

        var controller = new RuntimeControlsController(
            control: control,
            runtimeIdentity: runtime,
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true, evaluateCallCountHolder: evaluateCalls),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            environment: new StubEnvironmentContext("local"));

        var result = await controller.RequestShutdown("rt-1", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        evaluateCalls.Value.Should().Be(1);
        runtime.GetByIdCallCount.Should().Be(1);
        control.RequestShutdownCalls.Should().Be(0);
    }

    [Fact]
    public async Task Controller_WhenRuntimeLacksRequiredCapability_Returns409()
    {
        var control = new StubRuntimeControlService();
        var runtime = new StubRuntimeIdentityService(runtimeIdentity: new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.InspectionCapabilities],
            DateTimeOffset.UtcNow));

        var evaluateCalls = new EvaluateCallCountHolder();

        var controller = new RuntimeControlsController(
            control: control,
            runtimeIdentity: runtime,
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true, evaluateCallCountHolder: evaluateCalls),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            environment: new StubEnvironmentContext("local"));

        var result = await controller.RequestShutdown("rt-1", CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        runtime.GetByIdCallCount.Should().Be(1);
        control.RequestShutdownCalls.Should().Be(0);
    }

    [Fact]
    public async Task Controller_WhenAuthorizedAndEligible_ReturnsAccepted_AndInvokesControlService()
    {
        var control = new StubRuntimeControlService(returnValue: new RuntimeControlRequestResult(
            Id: 10,
            RuntimeId: "rt-1",
            ControlKind: RuntimeControlKindNames.Shutdown,
            Status: RuntimeControlStatusNames.Queued,
            RequestedAt: DateTimeOffset.UtcNow,
            RequestedBy: "u1",
            AppliedAt: null,
            AppliedBy: null,
            LastError: null));

        var runtime = new StubRuntimeIdentityService(runtimeIdentity: new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow));

        var evaluateCalls = new EvaluateCallCountHolder();
        var auth = new StubAuthorizationEvaluationService(allowed: true, evaluateCallCountHolder: evaluateCalls);

        var controller = new RuntimeControlsController(
            control: control,
            runtimeIdentity: runtime,
            authorizationEvaluation: auth,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            environment: new StubEnvironmentContext("local"));

        var result = await controller.RequestShutdown("rt-1", CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        control.RequestShutdownCalls.Should().Be(1);
        runtime.GetByIdCallCount.Should().Be(1);
        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.RuntimeControlShutdownRequest);
        auth.LastEvaluatedResource!.ResourceType.Should().Be(ResourceTypeConstants.RuntimeInstance);
        auth.LastEvaluatedResource.ScopeKey.Should().Be(ScopeKeyConstants.RuntimeInstance("local", "rt-1"));

        var accepted = result.Should().BeOfType<AcceptedResult>().Which;
        var value = accepted.Value!;
        var requestId = (long?)value.GetType().GetProperty("requestId", BindingFlags.Public | BindingFlags.Instance)!.GetValue(value);
        requestId.Should().Be(10);
        var status = (string?)value.GetType().GetProperty("status", BindingFlags.Public | BindingFlags.Instance)!.GetValue(value);
        status.Should().Be(RuntimeControlStatusNames.Queued);
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_PersistsQueuedRequest_AndAppendsAudit()
    {
        var db = NewDb();
        var runtimeIdentity = new StubRuntimeIdentityServiceForService(runtimeIdentity: new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow));

        var actor = new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false);
        var principalContext = new StubPrincipalContextForService(actor);
        var environment = new StubEnvironmentContext("local");
        var audit = new StubAuditService();

        var service = new RuntimeControlService(
            db: db,
            runtimeIdentity: runtimeIdentity,
            principalContext: principalContext,
            environment: environment,
            audit: audit);

        var result = await service.RequestShutdownAsync("rt-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(RuntimeControlStatusNames.Queued);

        var row = await db.RuntimeControlRequests.FirstAsync();
        row.RuntimeId.Should().Be("rt-1");
        row.ControlKind.Should().Be(RuntimeControlKindNames.Shutdown);
        row.Status.Should().Be(RuntimeControlStatusNames.Queued);
        row.RequestedBy.Should().Be("u1");

        audit.AppendCalls.Should().Be(1);
        audit.LastRecord!.Action.Should().Be(GovernanceAuditActionNames.RuntimeShutdownRequestCreated);
        audit.LastRecord!.Resource!.ResourceType.Should().Be(ResourceTypeConstants.RuntimeInstance);
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_IsIdempotent_ForQueuedRequests()
    {
        var db = NewDb();
        var runtimeIdentity = new StubRuntimeIdentityServiceForService(runtimeIdentity: new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow));

        var actor = new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false);
        var principalContext = new StubPrincipalContextForService(actor);
        var environment = new StubEnvironmentContext("local");
        var audit = new StubAuditService();

        var service = new RuntimeControlService(db, runtimeIdentity, principalContext, environment, audit);

        var first = await service.RequestShutdownAsync("rt-1", CancellationToken.None);
        var second = await service.RequestShutdownAsync("rt-1", CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        audit.AppendCalls.Should().Be(1);
        (await db.RuntimeControlRequests.ToListAsync()).Count.Should().Be(1);
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class EvaluateCallCountHolder
    {
        public int Value { get; set; }
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;
        private readonly EvaluateCallCountHolder _holder;

        public PermissionDescriptor? LastEvaluatedPermission { get; private set; }
        public ResourceDescriptor? LastEvaluatedResource { get; private set; }

        public StubAuthorizationEvaluationService(bool allowed, EvaluateCallCountHolder evaluateCallCountHolder)
        {
            _allowed = allowed;
            _holder = evaluateCallCountHolder;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            _holder.Value++;
            LastEvaluatedPermission = context.Permission;
            LastEvaluatedResource = context.Resource;
            return ValueTask.FromResult(_allowed);
        }
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;
        public StubPrincipalContext(IActor? actor) => _actor = actor;
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }

    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId { get; }
        public StubEnvironmentContext(string environmentId) => EnvironmentId = environmentId;
    }

    private sealed class StubRuntimeIdentityService : IRuntimeIdentityService
    {
        private readonly RuntimeIdentity? _runtimeIdentity;
        public int GetByIdCallCount { get; private set; }

        public StubRuntimeIdentityService(RuntimeIdentity? runtimeIdentity) => _runtimeIdentity = runtimeIdentity;

        public ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;
            return ValueTask.FromResult(_runtimeIdentity);
        }

        public ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeIdentity>>(Array.Empty<RuntimeIdentity>());

        public ValueTask<RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeTrustValidationResult(false, FailureReason: "not implemented"));

        public ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentResult(request.RuntimeId, false, "not implemented"));
    }

    private sealed class StubRuntimeControlService : IRuntimeControlService
    {
        private readonly RuntimeControlRequestResult? _returnValue;

        public int RequestShutdownCalls { get; private set; }

        public StubRuntimeControlService(RuntimeControlRequestResult? returnValue = null)
        {
            _returnValue = returnValue;
        }

        public ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            RequestShutdownCalls++;
            return ValueTask.FromResult(_returnValue);
        }

        public ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeControlRequestResult>>(Array.Empty<RuntimeControlRequestResult>());
    }

    private sealed class StubPrincipalContextForService : IPrincipalContext
    {
        private readonly IActor _actor;
        public StubPrincipalContextForService(IActor actor) => _actor = actor;
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(_actor);
    }

    private sealed class StubRuntimeIdentityServiceForService : IRuntimeIdentityService
    {
        private readonly RuntimeIdentity? _runtimeIdentity;
        public StubRuntimeIdentityServiceForService(RuntimeIdentity? runtimeIdentity) => _runtimeIdentity = runtimeIdentity;

        public ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runtimeIdentity);

        public ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeIdentity>>(Array.Empty<RuntimeIdentity>());

        public ValueTask<RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeTrustValidationResult(false, FailureReason: "not implemented"));

        public ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentResult(request.RuntimeId, false, "not implemented"));
    }

    private sealed class StubAuditService : IAuditService
    {
        public int AppendCalls { get; private set; }
        public AuditRecord? LastRecord { get; private set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            LastRecord = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage(Array.Empty<AuditRecord>(), null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }
}

