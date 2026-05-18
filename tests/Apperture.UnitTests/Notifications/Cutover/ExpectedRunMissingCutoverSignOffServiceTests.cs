using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Notifications.Cutover;

public sealed class ExpectedRunMissingCutoverSignOffServiceTests
{
    [Fact]
    public async Task Create_ready_sign_off_when_parity_acceptable()
    {
        var harness = CreateHarness(parityAcceptable: true);
        var result = await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover, "ok"),
            CancellationToken.None);

        result.Status.Should().Be(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover);
        result.ParityAcceptableAtReview.Should().BeTrue();
        harness.AuditRecords.Should().ContainSingle(r =>
            r.Action == NotificationAuditActionNames.ExpectedRunMissingCutoverSignedOff);
    }

    [Fact]
    public async Task Create_ready_sign_off_rejected_when_blocking_parity()
    {
        var harness = CreateHarness(parityAcceptable: false, blocking: ["missing_in_platform"]);
        Func<Task> act = async () => await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover, null),
            CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_not_ready_sign_off_allowed_with_blocking_parity()
    {
        var harness = CreateHarness(parityAcceptable: false, blocking: ["missing_in_platform"]);
        var result = await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.NotReady, "hold"),
            CancellationToken.None);

        result.Status.Should().Be(ExpectedRunMissingCutoverSignOffStatuses.NotReady);
        harness.AuditRecords.Should().ContainSingle(r =>
            r.Action == NotificationAuditActionNames.ExpectedRunMissingCutoverSignedOff);
    }

    [Fact]
    public async Task Revoke_emits_audit_and_appends_revoked_row()
    {
        var harness = CreateHarness(parityAcceptable: true);
        await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover, null),
            CancellationToken.None);

        var revoked = await harness.Service.RevokeSignOffAsync("int-1", "changed mind", CancellationToken.None);
        revoked.Status.Should().Be(ExpectedRunMissingCutoverSignOffStatuses.Revoked);
        harness.AuditRecords.Should().Contain(r =>
            r.Action == NotificationAuditActionNames.ExpectedRunMissingCutoverSignOffRevoked);
    }

    [Fact]
    public async Task GetLatest_returns_most_recent_sign_off()
    {
        var harness = CreateHarness(parityAcceptable: true);
        await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.NotReady, null),
            CancellationToken.None);
        await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover, null),
            CancellationToken.None);

        var latest = await harness.Service.GetLatestAsync("int-1", CancellationToken.None);
        latest!.Status.Should().Be(ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover);
        harness.AuditRecords.Should().Contain(r =>
            r.Action == NotificationAuditActionNames.ExpectedRunMissingCutoverSignOffReplaced);
    }

    [Fact]
    public async Task Manage_required_for_sign_off()
    {
        var harness = CreateHarness(parityAcceptable: true, allowManage: false);
        Func<Task> act = async () => await harness.Service.CreateOrReplaceSignOffAsync(
            "int-1",
            new CreateExpectedRunMissingCutoverSignOffRequest(ExpectedRunMissingCutoverSignOffStatuses.NotReady, null),
            CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static TestHarness CreateHarness(
        bool parityAcceptable,
        IReadOnlyList<string>? blocking = null,
        bool allowManage = true)
    {
        var store = new InMemoryCutoverSignOffStore();
        var diagnostics = new StubDiagnostics(parityAcceptable, blocking ?? []);
        var auth = new NotificationDefinitionAuthorization(
            new FixedPrincipalContext(new StubActor("user:op", "Operator")),
            new AllowAuth(),
            new IntegrationUpdateAuthEval(allowManage),
            new StubIntegrationRepo());
        var audit = new CapturingAuditService();
        var service = new ExpectedRunMissingCutoverSignOffService(
            store,
            diagnostics,
            auth,
            new FixedPrincipalContext(new StubActor("user:op", "Operator")),
            audit,
            TimeProvider.System);
        return new TestHarness(service, audit.Records);
    }

    private sealed record TestHarness(
        ExpectedRunMissingCutoverSignOffService Service,
        List<AuditRecord> AuditRecords);

    private sealed class InMemoryCutoverSignOffStore : IExpectedRunMissingCutoverSignOffStore
    {
        private readonly List<ExpectedRunMissingCutoverSignOff> _rows = [];

        public ValueTask<ExpectedRunMissingCutoverSignOff?> GetLatestAsync(
            string integrationId,
            string notificationDefinitionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExpectedRunMissingCutoverSignOff?>(
                _rows
                    .Where(r => r.IntegrationId == integrationId && r.NotificationDefinitionId == notificationDefinitionId)
                    .OrderByDescending(r => r.ReviewedAtUtc)
                    .FirstOrDefault());

        public ValueTask<ExpectedRunMissingCutoverSignOff> AppendAsync(
            ExpectedRunMissingCutoverSignOff signOff,
            CancellationToken cancellationToken = default)
        {
            _rows.Add(signOff);
            return ValueTask.FromResult(signOff);
        }
    }

    private sealed class StubDiagnostics : INotificationDeliveryDiagnosticsService
    {
        private readonly bool _acceptable;
        private readonly IReadOnlyList<string> _blocking;

        public StubDiagnostics(bool acceptable, IReadOnlyList<string> blocking)
        {
            _acceptable = acceptable;
            _blocking = blocking;
        }

        public ValueTask<NotificationConfigurationDiagnostics> GetConfigurationDiagnosticsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new NotificationConfigurationDiagnostics(false, false, false, false, false, [], DateTimeOffset.UtcNow));

        public ValueTask<ExpectedRunMissingParityAcceptanceSummary?> GetExpectedRunMissingParityAcceptanceAsync(
            string integrationId,
            string? attentionIncidentId,
            int maxIncidents,
            int takePerIncident,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExpectedRunMissingParityAcceptanceSummary?>(
                new ExpectedRunMissingParityAcceptanceSummary(
                    integrationId,
                    1,
                    1,
                    _acceptable ? 1 : 0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    _blocking,
                    [],
                    _acceptable,
                    DateTimeOffset.UtcNow));

        public ValueTask<NotificationDeliveryHealthSummary> GetHealthSummaryAsync(
            string? definitionId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<NotificationIntegrationDiagnostics> GetByIntegrationAsync(
            string integrationId,
            string? definitionId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<NotificationIncidentDiagnostics?> GetByAttentionIncidentAsync(
            string attentionIncidentId,
            string? definitionId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<ExpectedRunMissingParityDiagnostics?> GetExpectedRunMissingParityAsync(
            string? integrationId,
            string? attentionIncidentId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class AllowAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? governedResource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class IntegrationUpdateAuthEval : IAuthorizationEvaluationService
    {
        private readonly bool _allowManage;
        public IntegrationUpdateAuthEval(bool allowManage) => _allowManage = allowManage;

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (context.Permission.Action == ActionConstants.IntegrationUpdate)
                return ValueTask.FromResult(_allowManage);
            if (context.Permission.Action == ActionConstants.IntegrationRead)
                return ValueTask.FromResult(true);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StubIntegrationRepo : IIntegrationRepository
    {
        public ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainIntegration?>(
                new DomainIntegration(
                    id,
                    "Test",
                    "mod-1",
                    null,
                    IntegrationState.Active,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));

        public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DomainIntegration>>([]);

        public ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        private readonly IActor _actor;
        public FixedPrincipalContext(IActor actor) => _actor = actor;
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(_actor);
    }

    private sealed class StubActor : IActor
    {
        public StubActor(string id, string? name)
        {
            Id = new ActorId(id);
            DisplayName = name;
        }

        public ActorId Id { get; }
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName { get; }
    }
}
