using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using Apperture.UnitTests.Integration;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

/// <summary>
/// LFC-1.1: disabled schedules can be hard-deleted. Enabled schedules cannot. History stays.
/// </summary>
public sealed class ScheduleDeleteTests
{
    [Fact]
    public async Task Disabled_schedule_removes_definition_assignments_and_cursors_and_keeps_history()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "15 3 * * *");
        var other = await h.CreateDisabledStandaloneAsync("Weekly", "0 1 * * 1");
        var assignment = await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
        await h.Schedules.AssignToRuntimePlaneAsync(other.Id, new RuntimePlaneId("local"), null);
        h.Db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = assignment.Id.Value,
            LastOutcome = "Succeeded",
            LastAttemptedUtc = DateTimeOffset.Parse("2026-01-01T03:15:00Z")
        });
        var fire = DateTimeOffset.Parse("2026-01-01T03:15:00Z");
        h.Db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = schedule.Id.Value,
            AssignmentId = assignment.Id.Value,
            FireTimeUtc = fire,
            StartedAtUtc = fire,
            Outcome = "Succeeded",
            RunId = "run-1",
            ResourceContextId = ResourceContextIds.SharedValue
        });
        var idempotencyKey = $"schedule:{schedule.Id.Value}:{fire:O}";
        h.Db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "run-1",
            OperationInstanceId = "op-1",
            OperationDefinitionId = "proc-1",
            IdempotencyKey = idempotencyKey,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = fire,
            ResourceContextId = ResourceContextIds.SharedValue
        });
        h.Db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ni-1",
            NotificationDefinitionId = "nd-1",
            NotificationKind = "platform",
            Status = "Delivered",
            ScheduleId = schedule.Id.Value,
            InstanceDedupeKey = "dedupe-1",
            RenderedSubject = "subject",
            RenderedSummary = "summary",
            RenderedBodyText = "body",
            TemplateVersionHash = "hash",
            RenderContextJson = "{}",
            CreatedAtUtc = fire,
            UpdatedAtUtc = fire
        });
        h.Db.AuditLogs.Add(new AuditLogEntity
        {
            At = fire,
            Actor = "system:scheduler",
            Action = "scheduler.schedule.dispatched",
            ScheduleId = schedule.Id.Value,
            Details = "prior firing"
        });
        await h.Db.SaveChangesAsync();

        await h.Schedules.DeleteScheduleAsync(schedule.Id);

        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == schedule.Id.Value)).Should().BeFalse();
        (await h.Db.ScheduleAssignments.AnyAsync(a => a.ScheduleId == schedule.Id.Value)).Should().BeFalse();
        (await h.Db.ScheduleStates.AnyAsync(s => s.AssignmentId == assignment.Id.Value)).Should().BeFalse();
        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == other.Id.Value)).Should().BeTrue();
        (await h.Db.ScheduleAssignments.CountAsync(a => a.ScheduleId == other.Id.Value)).Should().Be(1);

        var run = await h.Db.ScheduleRuns.SingleAsync();
        run.ScheduleId.Should().Be(schedule.Id.Value);
        run.Outcome.Should().Be("Succeeded");
        run.RunId.Should().Be("run-1");
        var start = await h.Db.OperationStartRequests.SingleAsync();
        start.IdempotencyKey.Should().Be(idempotencyKey);
        RunOriginClassifier.TryParseScheduleDispatchIdempotency(start.IdempotencyKey)
            .Should().Be((schedule.Id.Value, fire));
        (await h.Db.NotificationInstances.SingleAsync()).ScheduleId.Should().Be(schedule.Id.Value);
        (await h.Db.AuditLogs.SingleAsync()).Action.Should().Be("scheduler.schedule.dispatched");

        var deleted = h.Audit.Single(AuditActionNames.ScheduleDeleted);
        deleted.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        deleted.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, schedule.Id.Value));
        deleted.Related.ScheduleId.Should().Be(schedule.Id.Value);
        deleted.Related.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
        deleted.Fields["name"].Should().Be("Nightly");
        deleted.Fields["assignmentsRemoved"].Should().Be(1);
        deleted.Fields.Should().NotContainKey("integrationId");
        string.Join("|", deleted.Fields.Values.Select(v => v?.ToString())).Should().NotContain("15 3", "the recurrence is never recorded");
        h.Audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleted }, "exactly one primary row and no rejection row");

        var again = async () => await h.Schedules.DeleteScheduleAsync(schedule.Id);
        await again.Should().ThrowAsync<ScheduleNotFoundException>().WithMessage("*not found*");
        h.Audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleted });

        var act = async () => await h.Repository.UpdateStateAsync(assignment.Id.Value, new ScheduleStateUpdate(null, DateTimeOffset.UtcNow, "Succeeded", null, null));
        await act.Should().NotThrowAsync();
        (await h.Db.ScheduleStates.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Enabled_schedule_is_rejected_and_audited_without_removing_rows()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "15 3 * * *");
        await h.Schedules.EnableAsync(schedule.Id);
        var assignment = await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);

        var act = async () => await h.Schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>()
            .WithMessage(ScheduleDeleteRejectedException.EnabledMessage);
        (await h.Db.ScheduleDefinitions.SingleAsync()).Enabled.Should().BeTrue();
        (await h.Db.ScheduleAssignments.AnyAsync(a => a.Id == assignment.Id.Value)).Should().BeTrue();
        var rejected = h.Audit.Single(AuditActionNames.ScheduleDeleteRejected);
        rejected.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        rejected.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, schedule.Id.Value));
        rejected.Related.ScheduleId.Should().Be(schedule.Id.Value);
        rejected.Fields["reason"].Should().Be("enabled");
        rejected.Fields["name"].Should().Be("Nightly");
        string.Join("|", rejected.Fields.Values.Select(v => v?.ToString())).Should().NotContain("15 3");
        h.Audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleteRejected });
        h.Audit.Unit.Staged.Should().BeEmpty("the attempt declared a no-op and staged nothing");
    }

    [Fact]
    public async Task A_failed_rejection_row_does_not_change_the_refusal()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "15 3 * * *");
        await h.Schedules.EnableAsync(schedule.Id);
        h.Audit.FailWrites = true;

        var act = async () => await h.Schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>().WithMessage(ScheduleDeleteRejectedException.EnabledMessage);
        h.Audit.Actions.Should().BeEmpty("the W3 write failed and nothing else was recorded");
        (await h.Db.ScheduleDefinitions.SingleAsync()).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_schedule_writes_no_delete_audit()
    {
        await using var h = new Harness();

        var act = async () => await h.Schedules.DeleteScheduleAsync(new ScheduleId("missing"));

        await act.Should().ThrowAsync<ScheduleNotFoundException>().WithMessage("*not found*");
        h.Audit.Actions.Should().BeEmpty();
        h.Audit.Unit.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Unauthorized_caller_does_not_delete_or_audit()
    {
        await using var h = new Harness(anonymous: true);
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");

        var act = async () => await h.Schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == schedule.Id.Value)).Should().BeTrue();
        h.Audit.Actions.Should().BeEmpty();
        h.Audit.Unit.Operations.Should().BeEmpty("a missing actor fails before the unit opens");
        h.Gate.Acquires.Should().Be(0);
    }

    [Fact]
    public async Task Disabled_schedule_on_an_archived_application_can_be_deleted_and_enable_stays_forbidden()
    {
        await using var h = new Harness();
        var application = await h.ResourceContexts.CreateApplicationContextAsync("Campus");
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *", application.Id);
        var kept = await h.CreateDisabledStandaloneAsync("Kept", "0 1 * * *", application.Id);
        await h.ResourceContexts.ArchiveAsync(application.Id);

        await h.Schedules.DeleteScheduleAsync(schedule.Id);

        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == schedule.Id.Value)).Should().BeFalse();
        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == kept.Id.Value)).Should().BeTrue();
        var enable = async () => await h.Schedules.EnableAsync(kept.Id);
        await enable.Should().ThrowAsync<ResourceContextArchivedException>();
    }

    [Fact]
    public async Task Delete_observes_enable_that_commits_as_the_gate_is_acquired()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
        var flipping = new Harness.EnableOnAcquireGate(h.Db, schedule.Id.Value);
        var schedules = h.WithGate(flipping);

        var act = async () => await schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>();
        flipping.Acquires.Should().Be(1);
        var row = await h.Db.ScheduleDefinitions.SingleAsync();
        row.Enabled.Should().BeTrue();
        (await h.Db.ScheduleAssignments.CountAsync()).Should().Be(1);
        h.Audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleteRejected });
    }

    [Fact]
    public async Task The_gate_is_taken_inside_the_unit_before_anything_is_staged()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        var observing = new Harness.ObservingGate(h.Audit);

        await h.WithGate(observing).DeleteScheduleAsync(schedule.Id);

        observing.Acquires.Should().Be(1);
        observing.OperationsWhenAcquired.Should().Be(1, "the gate is acquired inside the audited unit, after its operation lock");
        observing.StagedWhenAcquired.Should().Be(0, "the primary row is staged after the gate and the row lock");
        h.Audit.Unit.Operations.Should().ContainSingle().Which.Operation.PrimaryAction.Should().Be(AuditActionNames.ScheduleDeleted);
    }

    [Fact]
    public async Task A_lost_acknowledgement_reconstructs_the_delete_without_running_it_twice()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
        h.Audit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        await h.Schedules.DeleteScheduleAsync(schedule.Id);

        h.Audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleted });
        h.Audit.Unit.Operations.Should().ContainSingle("the attempt ran once; the result came from the committed row");
        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == schedule.Id.Value)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        h.Audit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = async () => await h.Schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        h.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_audited_unit_a_delete_is_an_audit_invariant_not_a_missing_schedule()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        var withoutUnit = h.WithoutUnit();

        var act = async () => await withoutUnit.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<AuditInvariantException>();
        (await h.Db.ScheduleDefinitions.AnyAsync(s => s.Id == schedule.Id.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task Assign_after_delete_does_not_recreate_an_assignment()
    {
        await using var h = new Harness();
        var schedule = await h.CreateDisabledStandaloneAsync("Nightly", "0 0 * * *");
        await h.Schedules.DeleteScheduleAsync(schedule.Id);

        var act = async () => await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        (await h.Db.ScheduleAssignments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Integration_delete_is_blocked_until_the_disabled_schedule_is_deleted()
    {
        using var integrations = new IntegrationLifecycleTestHarness();
        var integration = await integrations.CreateIntegrationAsync();
        var audit = new AuditRecording();
        var schedules = new SqlScheduleManagementService(
            new SqlScheduleRepository(integrations.Db),
            new Harness.StubClaimOptions(),
            TimeProvider.System,
            new ResourceContextService(new SqlResourceContextRepository(integrations.Db), TimeProvider.System),
            new SqlIntegrationRepository(integrations.Db),
            integrations.Db,
            new SqlResourceOwnershipMutationGate(integrations.Db),
            new Harness.FixedPrincipal(),
            audit,
            audit);
        var schedule = await schedules.CreateScheduleAsync(new ScheduleDefinition(
            new ScheduleId("sch-attached"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            integration.Id));
        integrations.Db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = schedule.Id.Value,
            AssignmentId = "none",
            FireTimeUtc = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            StartedAtUtc = DateTimeOffset.Parse("2026-02-01T00:00:01Z"),
            Outcome = "Succeeded"
        });
        await integrations.Db.SaveChangesAsync();

        var blocked = async () => await integrations.Integrations.DeleteIntegrationAsync(integration.Id);
        await blocked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*schedule*");
        (await integrations.Db.Integrations.AnyAsync(i => i.Id == integration.Id.Value)).Should().BeTrue();

        await schedules.DeleteScheduleAsync(schedule.Id);

        await integrations.Integrations.DeleteIntegrationAsync(integration.Id);
        (await integrations.Db.Integrations.AnyAsync(i => i.Id == integration.Id.Value)).Should().BeFalse();
        (await integrations.Db.ScheduleDefinitions.AnyAsync()).Should().BeFalse();
        (await integrations.Db.ScheduleRuns.SingleAsync()).ScheduleId.Should().Be(schedule.Id.Value);
        integrations.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationDeleted);
        integrations.Audit.Records.Should().NotContain(r => r.Action == ActionConstants.ScheduleDeleted);
        audit.Actions.Should().Equal(new[] { AuditActionNames.ScheduleDeleted });
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IPrincipalContext _principal;

        public Harness(bool anonymous = false)
        {
            _principal = new FixedPrincipal(anonymous: anonymous);
            Db = new AppertureDbContext(
                new DbContextOptionsBuilder<AppertureDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options);
            Db.ResourceContexts.Add(new ResourceContextEntity
            {
                Id = ResourceContextIds.SharedValue,
                Name = "Shared",
                Slug = "shared",
                Kind = ResourceContextKinds.Shared,
                State = ResourceContextStates.Active,
                IsReserved = true,
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                UpdatedAtUtc = DateTimeOffset.UnixEpoch
            });
            Db.SaveChanges();

            Audit = new AuditRecording();
            Gate = new CountingGate();
            Repository = new SqlScheduleRepository(Db);
            ResourceContexts = new ResourceContextService(new SqlResourceContextRepository(Db), TimeProvider.System);
            Schedules = Build(Gate);
        }

        public AppertureDbContext Db { get; }
        public AuditRecording Audit { get; }
        public CountingGate Gate { get; }
        public SqlScheduleRepository Repository { get; }
        public IResourceContextService ResourceContexts { get; }
        public IScheduleManagementService Schedules { get; }

        public IScheduleManagementService WithGate(IResourceOwnershipMutationGate gate) => Build(gate);

        public IScheduleManagementService WithoutUnit() =>
            new SqlScheduleManagementService(
                Repository,
                new StubClaimOptions(),
                TimeProvider.System,
                ResourceContexts,
                new SqlIntegrationRepository(Db),
                Db,
                Gate,
                _principal);

        public async Task<ScheduleDefinition> CreateDisabledStandaloneAsync(string name, string cron, ResourceContextId? contextId = null)
        {
            // Setup uses a principal even when the test actor is null, so create itself is not the assertion.
            var setup = new SqlScheduleManagementService(
                Repository,
                new StubClaimOptions(),
                TimeProvider.System,
                ResourceContexts,
                new SqlIntegrationRepository(Db),
                Db,
                new SqlResourceOwnershipMutationGate(Db),
                new FixedPrincipal(new FixedActor()));
            return await setup.CreateScheduleAsync(new ScheduleDefinition(
                new ScheduleId($"s-{Guid.NewGuid():N}"),
                name,
                cron,
                "UTC",
                Enabled: false,
                new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                ExecutionPolicyRef: null,
                Version: 1,
                IntegrationId: null)
            { ResourceContextId = contextId });
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();

        private SqlScheduleManagementService Build(IResourceOwnershipMutationGate gate) =>
            new(
                Repository,
                new StubClaimOptions(),
                TimeProvider.System,
                ResourceContexts,
                new SqlIntegrationRepository(Db),
                Db,
                gate,
                _principal,
                Audit,
                Audit);

        internal sealed class CountingGate : IResourceOwnershipMutationGate
        {
            public int Acquires { get; private set; }
            public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
            {
                Acquires++;
                return ValueTask.CompletedTask;
            }
        }

        internal sealed class EnableOnAcquireGate : IResourceOwnershipMutationGate
        {
            private readonly AppertureDbContext _db;
            private readonly string _scheduleId;

            public EnableOnAcquireGate(AppertureDbContext db, string scheduleId)
            {
                _db = db;
                _scheduleId = scheduleId;
            }

            public int Acquires { get; private set; }

            public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
            {
                Acquires++;
                var row = await _db.ScheduleDefinitions.SingleAsync(s => s.Id == _scheduleId, cancellationToken);
                row.Enabled = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        /// <summary>Records how far the audited unit had got when the gate was acquired.</summary>
        internal sealed class ObservingGate(AuditRecording audit) : IResourceOwnershipMutationGate
        {
            public int Acquires { get; private set; }
            public int OperationsWhenAcquired { get; private set; }
            public int StagedWhenAcquired { get; private set; }

            public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
            {
                Acquires++;
                OperationsWhenAcquired = audit.Unit.Operations.Count;
                StagedWhenAcquired = audit.Unit.Operations.Sum(o => o.Staged.Count);
                return ValueTask.CompletedTask;
            }
        }

        internal sealed class StubClaimOptions : ISchedulerClaimOptions
        {
            public int ClaimLeaseMinutes => 5;
        }

        internal sealed class FixedPrincipal : IPrincipalContext
        {
            private readonly IActor? _actor;
            private readonly bool _anonymous;

            public FixedPrincipal(IActor? actor = null, bool anonymous = false)
            {
                _actor = actor;
                _anonymous = anonymous;
            }

            public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(_anonymous ? null : _actor ?? new FixedActor());
        }

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("schedule-delete-test");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Schedule Delete Test";
        }
    }
}
