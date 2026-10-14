using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// SEC-2.1: the acknowledgement of an attention incident as one audited unit. These tests run the real service over EF Core
/// InMemory with the recording unit, which proves what the writer decides to write and stage. The transaction, the integration
/// and incident row locks and the commit guarantees are proven against SQL Server in
/// <c>AttentionIncidentAcknowledgementSqlServerTests</c>.
/// </summary>
public sealed class AttentionIncidentAcknowledgementServiceTests : IDisposable
{
    private const string NoteSentinel = "note-sentinel-do-not-record";
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2025-01-10T14:00:00Z");

    private readonly AppertureDbContext _db = new(
        new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private readonly RecordingAuditedUnitOfWork _audit = new();
    private readonly SqlIntegrationRepository _integrations;
    private readonly AttentionIncidentAcknowledgementService _sut;
    private int _reauthorizations;

    public AttentionIncidentAcknowledgementServiceTests()
    {
        _integrations = new SqlIntegrationRepository(_db);
        _sut = new AttentionIncidentAcknowledgementService(_db, _integrations, _audit, new FixedTime(Now));
    }

    public void Dispose() => _db.Dispose();

    // ---- the first acknowledgement ----

    [Fact]
    public async Task A_first_acknowledgement_writes_the_three_columns_and_stages_one_row()
    {
        var (integration, incidentId) = await ArrangeAsync();

        var outcome = await AckAsync(integration, incidentId, note: "  " + NoteSentinel + "  ");

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
        var row = await IncidentAsync(incidentId);
        row.AcknowledgedAtUtc.Should().Be(Now);
        row.AcknowledgedBy.Should().Be("usr_unit_test_actor", "the actor is the unit's recorded actor, never request input");
        row.AcknowledgementNote.Should().Be(NoteSentinel, "the note is stored trimmed");
        row.Status.Should().Be(AttentionIncidentStatuses.Active);

        var operation = _audit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
        operation.Operation.Actor.IsCurrentPrincipal.Should().BeTrue("the actor comes from the authenticated context");
        var staged = _audit.Single(AuditActionNames.AttentionIncidentAcknowledged);
        staged.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, integration.Id.Value));
        staged.Fields["name"].Should().Be("Sample Integration");
        staged.Fields["incidentId"].Should().Be(incidentId);
        staged.Fields["conditionKey"].Should().Be("expected_run_missing");
        staged.Fields["incidentStartedAt"].Should().Be(Started);
        staged.Fields["noteProvided"].Should().Be(true);
        staged.Fields.Keys.Should().BeEquivalentTo("name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided");
        staged.Related.ResourceContextId.Should().Be(integration.ResourceContextId.Value);
        JsonSerializer.Serialize(staged.Fields).Should().NotContain(NoteSentinel);
    }

    [Fact]
    public async Task A_blank_note_is_stored_as_null_and_recorded_as_not_provided()
    {
        var (integration, incidentId) = await ArrangeAsync();

        await AckAsync(integration, incidentId, note: "   ");

        (await IncidentAsync(incidentId)).AcknowledgementNote.Should().BeNull();
        _audit.Single(AuditActionNames.AttentionIncidentAcknowledged).Fields["noteProvided"].Should().Be(false);
    }

    [Fact]
    public async Task No_note_is_stored_as_null()
    {
        var (integration, incidentId) = await ArrangeAsync();

        await AckAsync(integration, incidentId, note: null);

        (await IncidentAsync(incidentId)).AcknowledgementNote.Should().BeNull();
        _audit.Single(AuditActionNames.AttentionIncidentAcknowledged).Fields["noteProvided"].Should().Be(false);
    }

    [Fact]
    public async Task A_note_of_exactly_the_column_width_is_accepted()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var note = new string('n', AttentionIncidentAcknowledgementBounds.NoteMaxLength);

        (await AckAsync(integration, incidentId, note)).Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);

        (await IncidentAsync(incidentId)).AcknowledgementNote.Should().Be(note);
    }

    [Fact]
    public async Task A_note_over_the_column_width_is_refused_before_anything_runs()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var note = new string('n', AttentionIncidentAcknowledgementBounds.NoteMaxLength + 1);

        (await AckAsync(integration, incidentId, note)).Should().Be(AttentionIncidentAcknowledgementOutcome.NoteTooLong);

        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
        _audit.Operations.Should().BeEmpty("a request the service refuses on its face opens no unit");
        _reauthorizations.Should().Be(0);
    }

    [Fact]
    public async Task The_length_limit_applies_to_the_trimmed_note()
    {
        var (integration, incidentId) = await ArrangeAsync();
        var padded = "  " + new string('n', AttentionIncidentAcknowledgementBounds.NoteMaxLength) + "  ";

        (await AckAsync(integration, incidentId, padded)).Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
    }

    // ---- repeating ----

    [Fact]
    public async Task A_repeated_acknowledgement_is_a_no_op_that_keeps_the_first_acknowledger_time_and_note()
    {
        var (integration, incidentId) = await ArrangeAsync();
        await AckAsync(integration, incidentId, "first note");
        var first = await IncidentAsync(incidentId);
        _audit.CurrentPrincipalActor = AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_second_operator"));
        var later = new AttentionIncidentAcknowledgementService(_db, _integrations, _audit, new FixedTime(Now.AddHours(3)));

        var outcome = await later.AcknowledgeAsync(integration.Id, incidentId, "second note", Reauthorize);

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.AlreadyAcknowledged);
        var after = await IncidentAsync(incidentId);
        after.AcknowledgedAtUtc.Should().Be(first.AcknowledgedAtUtc);
        after.AcknowledgedBy.Should().Be("usr_unit_test_actor");
        after.AcknowledgementNote.Should().Be("first note");
        _audit.Actions.Should().ContainSingle("the repeat records nothing");
        _audit.Operations.Last().NoOp.Should().BeTrue();
    }

    // ---- states ----

    [Fact]
    public async Task A_resolved_incident_is_not_active_and_nothing_is_written()
    {
        var (integration, incidentId) = await ArrangeAsync(resolved: true);

        (await AckAsync(integration, incidentId, "late")).Should().Be(AttentionIncidentAcknowledgementOutcome.NotActive);

        var row = await IncidentAsync(incidentId);
        row.AcknowledgedAtUtc.Should().BeNull();
        row.AcknowledgedBy.Should().BeNull();
        row.AcknowledgementNote.Should().BeNull();
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resolved_incident_that_was_acknowledged_while_active_is_still_not_active()
    {
        var (integration, incidentId) = await ArrangeAsync();
        await AckAsync(integration, incidentId, "while active");
        await ResolveAsync(incidentId);

        (await AckAsync(integration, incidentId, "again")).Should().Be(AttentionIncidentAcknowledgementOutcome.NotActive);

        (await IncidentAsync(incidentId)).AcknowledgementNote.Should().Be("while active", "the acknowledgement made while it was active is not lost");
        _audit.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_incident_is_not_found()
    {
        var (integration, _) = await ArrangeAsync();

        (await AckAsync(integration, "attn_missing", null)).Should().Be(AttentionIncidentAcknowledgementOutcome.NotFound);

        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_incident_of_another_integration_is_not_found_and_stays_unacknowledged()
    {
        var (_, incidentId) = await ArrangeAsync();
        var other = await NewIntegrationAsync();

        (await AckAsync(other, incidentId, null)).Should().Be(AttentionIncidentAcknowledgementOutcome.NotFound);

        (await IncidentAsync(incidentId)).AcknowledgedAtUtc.Should().BeNull();
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_integration_is_not_found_and_does_not_reauthorize()
    {
        (await _sut.AcknowledgeAsync(new IntegrationId("int-gone"), "attn_x", null, Reauthorize))
            .Should().Be(AttentionIncidentAcknowledgementOutcome.NotFound);

        _reauthorizations.Should().Be(0, "there is no integration to authorize against");
        _audit.Actions.Should().BeEmpty();
    }

    // ---- authorization under the lock ----

    [Fact]
    public async Task The_caller_check_is_run_once_inside_the_unit_and_a_refusal_writes_nothing()
    {
        var (integration, incidentId) = await ArrangeAsync();

        var act = () => _sut.AcknowledgeAsync(
            integration.Id, incidentId, NoteSentinel, _ => throw new UnauthorizedAccessException("no longer permitted"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        var row = await IncidentAsync(incidentId);
        row.AcknowledgedAtUtc.Should().BeNull();
        row.AcknowledgedBy.Should().BeNull();
        row.AcknowledgementNote.Should().BeNull();
        _audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task The_caller_check_runs_before_the_incident_is_read_so_a_refused_caller_learns_nothing_about_it()
    {
        var (integration, _) = await ArrangeAsync();

        var act = () => _sut.AcknowledgeAsync(
            integration.Id, "attn_unknown", null, _ => throw new UnauthorizedAccessException("no longer permitted"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>("a refused caller is told 403, not 404");
    }

    [Fact]
    public async Task The_caller_check_is_run_exactly_once_per_acknowledgement()
    {
        var (integration, incidentId) = await ArrangeAsync();

        await AckAsync(integration, incidentId, null);

        _reauthorizations.Should().Be(1);
    }

    // ---- M6 at the unit level ----

    [Fact]
    public async Task A_lost_acknowledgement_is_reported_as_the_acknowledgement_it_was()
    {
        var (integration, incidentId) = await ArrangeAsync();
        _audit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var outcome = await AckAsync(integration, incidentId, "kept");

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged, "the result is reconstructed from the committed row");
        _audit.Actions.Should().ContainSingle().Which.Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
    }

    [Fact]
    public async Task An_unknown_outcome_is_surfaced_and_the_row_is_not_counted_as_committed()
    {
        var (integration, incidentId) = await ArrangeAsync();
        _audit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => AckAsync(integration, incidentId, null);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        _audit.Actions.Should().BeEmpty();
    }

    // ---- arrangement ----

    private Task<AttentionIncidentAcknowledgementOutcome> AckAsync(DomainIntegration integration, string incidentId, string? note) =>
        _sut.AcknowledgeAsync(integration.Id, incidentId, note, Reauthorize);

    private ValueTask Reauthorize(CancellationToken cancellationToken)
    {
        _reauthorizations++;
        return ValueTask.CompletedTask;
    }

    private async Task<DomainIntegration> NewIntegrationAsync()
    {
        var integration = new DomainIntegration(
            new IntegrationId("int-" + Guid.NewGuid().ToString("N")[..8]),
            "Sample Integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch)
        {
            ResourceContextId = new Apperture.Core.Domain.ResourceContexts.ResourceContextId("shared"),
        };
        await _integrations.AddAsync(integration);
        return integration;
    }

    private async Task<(DomainIntegration Integration, string IncidentId)> ArrangeAsync(bool resolved = false)
    {
        var integration = await NewIntegrationAsync();
        var incidentId = "attn_" + Guid.NewGuid().ToString("N");
        _db.AttentionIncidents.Add(new AttentionIncidentEntity
        {
            Id = incidentId,
            IntegrationId = integration.Id.Value,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            Status = resolved ? AttentionIncidentStatuses.Resolved : AttentionIncidentStatuses.Active,
            StartedAtUtc = Started,
            LastObservedAtUtc = Started,
            ResolvedAtUtc = resolved ? Started.AddHours(1) : null,
            ResolutionReason = resolved ? AttentionIncidentResolutionReasons.ExpectationCleared : null,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (integration, incidentId);
    }

    private async Task ResolveAsync(string incidentId)
    {
        var row = await _db.AttentionIncidents.SingleAsync(e => e.Id == incidentId);
        row.Status = AttentionIncidentStatuses.Resolved;
        row.ResolvedAtUtc = Now.AddHours(1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Task<AttentionIncidentEntity> IncidentAsync(string incidentId) =>
        _db.AttentionIncidents.AsNoTracking().SingleAsync(e => e.Id == incidentId);

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
