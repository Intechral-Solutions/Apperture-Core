using System.Reflection;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Security.Trust;
using Apperture.Management.Api.Controllers;
using Apperture.UnitTests.Audit;
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

    // ---- The request as an audited unit (AUD-1.4 slice 7) ----

    private const string RuntimeId = "rt-1";

    private sealed class ServiceHarness
    {
        public ServiceHarness(bool declareCapability = true, bool seedIdentity = true, bool stubSeesRuntime = true)
        {
            Db = NewDb();
            Unit = new RecordingAuditedUnitOfWork();
            var scopes = declareCapability
                ? new[] { RuntimeCapabilityCatalog.ControlShutdown }
                : new[] { RuntimeCapabilityCatalog.InspectionCapabilities };
            if (seedIdentity)
            {
                Db.RuntimeIdentities.Add(new RuntimeIdentityEntity
                {
                    Id = RuntimeId,
                    DisplayName = "Runtime 1",
                    CredentialType = "PublicKey",
                    PublicKeyOrThumbprint = "pk",
                    DeclaredScopesJson = JsonSerializer.Serialize(scopes),
                    RegisteredAt = DateTimeOffset.UtcNow,
                });
                Db.SaveChanges();
            }

            // The identity service the pre-unit check uses; it can disagree with the row (a runtime removed or changed since).
            Identity = stubSeesRuntime
                ? new StubRuntimeIdentityServiceForService(new RuntimeIdentity(RuntimeId, "Runtime 1", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.ControlShutdown], DateTimeOffset.UtcNow))
                : new StubRuntimeIdentityServiceForService(runtimeIdentity: null);
            Service = new RuntimeControlService(Db, Identity, new StubEnvironmentContext("local"), Unit);
        }

        public AppertureDbContext Db { get; }
        public RecordingAuditedUnitOfWork Unit { get; }
        public StubRuntimeIdentityServiceForService Identity { get; }
        public RuntimeControlService Service { get; }
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_PersistsQueuedRequest_AndStagesExactlyOnePrimaryRow()
    {
        var h = new ServiceHarness();

        var result = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(RuntimeControlStatusNames.Queued);
        var row = await h.Db.RuntimeControlRequests.SingleAsync();
        row.RuntimeId.Should().Be(RuntimeId);
        row.ControlKind.Should().Be(RuntimeControlKindNames.Shutdown);
        row.Status.Should().Be(RuntimeControlStatusNames.Queued);
        result.Id.Should().Be(row.Id);

        var operation = h.Unit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.RuntimeShutdownRequestCreated);
        operation.Operation.Actor.IsCurrentPrincipal.Should().BeTrue("the actor is the authenticated principal, never a caller-supplied value");
        operation.NoOp.Should().BeFalse();

        var staged = h.Unit.Staged.Should().ContainSingle().Subject;
        staged.Action.Should().Be(AuditActionNames.RuntimeShutdownRequestCreated);
        staged.OutcomeCode.Should().Be(AuditOutcomeCodes.Requested);
        staged.Target.Should().Be(new AuditTarget(AuditTargetTypes.RuntimeInstance, ScopeKeyConstants.RuntimeInstance("local", RuntimeId)));
        staged.Fields.Keys.Should().BeEquivalentTo("runtimeId", "requestId", "controlKind");
        staged.Fields["runtimeId"].Should().Be(RuntimeId);
        staged.Fields["requestId"].Should().Be(row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        staged.Fields["controlKind"].Should().Be("shutdown");
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_RecordsTheUnitsActorAsRequestedBy()
    {
        var h = new ServiceHarness();
        h.Unit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_requester"));

        var result = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        var recorded = h.Unit.CurrentPrincipalActor.RecordedValue;
        recorded.Should().Be("usr_requester");
        result!.RequestedBy.Should().Be(recorded);
        (await h.Db.RuntimeControlRequests.SingleAsync()).RequestedBy.Should().Be(recorded);
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_ADuplicateReturnsTheQueuedRequestUnchanged_DeclaresANoOpAndStagesNothing()
    {
        var h = new ServiceHarness();
        h.Unit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_first"));
        var first = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        h.Unit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_second"));
        var second = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id);
        second.RequestedBy.Should().Be("usr_first", "an existing request is never re-attributed");
        h.Unit.Operations.Should().HaveCount(2);
        h.Unit.Operations[1].NoOp.Should().BeTrue();
        h.Unit.Operations[1].Staged.Should().BeEmpty();
        h.Unit.Staged.Should().ContainSingle("a duplicate request records nothing");
        (await h.Db.RuntimeControlRequests.ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_AnAppliedRequestDoesNotBlockANewOne()
    {
        var h = new ServiceHarness();
        var first = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);
        var row = await h.Db.RuntimeControlRequests.SingleAsync();
        row.Status = RuntimeControlStatusNames.Applied;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var again = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        again!.Id.Should().NotBe(first!.Id);
        h.Unit.Staged.Should().HaveCount(2);
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_UnknownRuntime_ReturnsNull_AndStagesNothing()
    {
        var h = new ServiceHarness(seedIdentity: false, stubSeesRuntime: false);

        var result = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        result.Should().BeNull();
        h.Unit.Operations.Should().BeEmpty("a runtime that does not exist is decided before the unit opens");
        (await h.Db.RuntimeControlRequests.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_RuntimeWithoutTheCapability_ReturnsNull_AndStagesNothing()
    {
        var h = new ServiceHarness(declareCapability: false);
        var withoutCapability = new RuntimeControlService(
            h.Db,
            new StubRuntimeIdentityServiceForService(new RuntimeIdentity(RuntimeId, "Runtime 1", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.InspectionCapabilities], DateTimeOffset.UtcNow)),
            new StubEnvironmentContext("local"),
            h.Unit);

        (await withoutCapability.RequestShutdownAsync(RuntimeId, CancellationToken.None)).Should().BeNull();
        h.Unit.Operations.Should().BeEmpty();

        // The identity service claimed the capability, but the row read under the lock does not declare it.
        (await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None)).Should().BeNull();
        h.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        h.Unit.Staged.Should().BeEmpty();
        (await h.Db.RuntimeControlRequests.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_ARuntimeRemovedAfterTheEarlyCheck_IsANoOpAndReturnsNull()
    {
        var h = new ServiceHarness(seedIdentity: false);

        (await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None)).Should().BeNull();

        h.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        h.Unit.Staged.Should().BeEmpty();
        (await h.Db.RuntimeControlRequests.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_ALostAcknowledgementReturnsTheRealRequestWithItsOriginalActor()
    {
        var h = new ServiceHarness();
        h.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        h.Unit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_requester"));

        var result = await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        var row = await h.Db.RuntimeControlRequests.SingleAsync();
        result.Should().BeEquivalentTo(new RuntimeControlRequestResult(
            row.Id, RuntimeId, RuntimeControlKindNames.Shutdown, RuntimeControlStatusNames.Queued, row.RequestedAt, "usr_requester", null, null, null));
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_AnUnknownOutcomePropagates()
    {
        var h = new ServiceHarness();
        h.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = async () => await h.Service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_ReconstructReadsTheRequestByTheIdTheRowRecords()
    {
        var db = NewDb();
        db.RuntimeControlRequests.Add(new RuntimeControlRequestEntity
        {
            RuntimeId = RuntimeId,
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Applied,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            RequestedBy = "usr_original",
            AppliedAt = DateTimeOffset.UtcNow,
            AppliedBy = "worker-1",
        });
        await db.SaveChangesAsync();
        var id = (await db.RuntimeControlRequests.SingleAsync()).Id;
        var unit = new ReconstructOnlyUnit(committedDetails: $"{{\"runtimeId\":\"{RuntimeId}\",\"requestId\":\"{id}\",\"controlKind\":\"shutdown\"}}");
        var service = new RuntimeControlService(
            db,
            new StubRuntimeIdentityServiceForService(new RuntimeIdentity(RuntimeId, "Runtime 1", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.ControlShutdown], DateTimeOffset.UtcNow)),
            new StubEnvironmentContext("local"),
            unit);

        var result = await service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        result!.Id.Should().Be(id);
        result.RequestedBy.Should().Be("usr_original", "the original actor, not the reconstructing caller");
        result.Status.Should().Be(RuntimeControlStatusNames.Applied, "a request the worker has since applied is still the request that was made");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"runtimeId\":\"rt-1\"}")]
    [InlineData("{\"requestId\":\"not-a-number\"}")]
    [InlineData("{\"requestId\":\"999999\"}")]
    public async Task Service_RequestShutdownAsync_ReconstructThatCannotReadTheRequestThrows(string? details)
    {
        var db = NewDb();
        var unit = new ReconstructOnlyUnit(details);
        var service = new RuntimeControlService(
            db,
            new StubRuntimeIdentityServiceForService(new RuntimeIdentity(RuntimeId, "Runtime 1", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.ControlShutdown], DateTimeOffset.UtcNow)),
            new StubEnvironmentContext("local"),
            unit);

        var act = async () => await service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("the unit reports a committed change whose result is unavailable");
    }

    [Fact]
    public async Task Service_RequestShutdownAsync_WithoutTheAuditedUnit_RefusesWithAnInvariantFailure()
    {
        var db = NewDb();
        var service = new RuntimeControlService(
            db,
            new StubRuntimeIdentityServiceForService(new RuntimeIdentity(RuntimeId, "Runtime 1", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.ControlShutdown], DateTimeOffset.UtcNow)),
            new StubEnvironmentContext("local"));

        var act = async () => await service.RequestShutdownAsync(RuntimeId, CancellationToken.None);

        await act.Should().ThrowAsync<AuditInvariantException>();
        (await db.RuntimeControlRequests.ToListAsync()).Should().BeEmpty();
    }

    /// <summary>A unit that runs only the reconstruction, with a primary row whose details are given: a commit whose acknowledgement was lost.</summary>
    private sealed class ReconstructOnlyUnit(string? committedDetails) : IAuditedUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            AuditedOperation operation,
            Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
            Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
            CancellationToken cancellationToken = default) =>
            reconstruct(
                new AuditCommittedOperation(
                    "event-1",
                    operation.PrimaryAction,
                    AuditTargetTypes.RuntimeInstance,
                    ScopeKeyConstants.RuntimeInstance("local", RuntimeId),
                    committedDetails),
                cancellationToken);
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
}
