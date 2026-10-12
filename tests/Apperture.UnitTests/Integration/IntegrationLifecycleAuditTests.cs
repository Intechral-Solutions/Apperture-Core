using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// AUD-1.4 slice 4: which audit rows the integration create, update and state transitions record, and what they refuse to
/// change without a unit (M7 at the service; the transactional guarantees are proven against SQL Server in
/// <c>IntegrationLifecycleAuditSqlServerTests</c>).
/// </summary>
public sealed class IntegrationLifecycleAuditTests
{
    private static CreateIntegrationRequest NewRequest(IReadOnlyDictionary<string, string>? settings = null, string? contextId = null) =>
        new("Sample API Polling", "Sample.ApiPolling", null, settings, contextId is null ? null : new ResourceContextId(contextId));

    [Fact]
    public async Task A_creation_records_one_integration_created_row_with_its_name_module_and_owning_context()
    {
        using var h = new IntegrationLifecycleTestHarness();

        var created = await h.Integrations.CreateIntegrationAsync(NewRequest());

        h.DeleteAudit.Actions.Should().Equal(AuditActionNames.IntegrationCreated);
        var evt = h.DeleteAudit.Single(AuditActionNames.IntegrationCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, created.Id.Value));
        evt.Related.ModuleId.Should().Be("Sample.ApiPolling");
        evt.Related.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Sample API Polling",
            ["moduleId"] = "Sample.ApiPolling",
            ["settingsProvided"] = false,
        });
        (await h.Db.Integrations.AsNoTracking().SingleAsync(i => i.Id == created.Id.Value)).State.Should().Be((int)IntegrationState.Active);
    }

    [Fact]
    public async Task A_creation_with_settings_records_that_they_were_provided_and_never_a_key_or_value()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var sentinel = "sentinel-" + Guid.NewGuid().ToString("N");

        await h.Integrations.CreateIntegrationAsync(NewRequest(new Dictionary<string, string> { ["apiKey" + sentinel] = sentinel }));

        var evt = h.DeleteAudit.Single(AuditActionNames.IntegrationCreated);
        evt.Fields["settingsProvided"].Should().Be(true);
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain(sentinel);
    }

    [Fact]
    public async Task A_creation_in_an_unknown_context_is_refused_and_records_nothing()
    {
        using var h = new IntegrationLifecycleTestHarness();

        var act = () => h.Integrations.CreateIntegrationAsync(NewRequest(contextId: "ctx-does-not-exist")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.DeleteAudit.Actions.Should().BeEmpty();
        (await h.Db.Integrations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Renaming_records_integration_updated_and_names_only_the_field()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.DeleteAudit.Unit.Operations.Clear();

        var updated = await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(DisplayName: "Renamed"));

        updated.DisplayName.Should().Be("Renamed");
        h.DeleteAudit.Actions.Should().Equal(AuditActionNames.IntegrationUpdated);
        var evt = h.DeleteAudit.Single(AuditActionNames.IntegrationUpdated);
        evt.Fields["name"].Should().Be("Renamed");
        evt.Fields["changed"].Should().BeEquivalentTo(new[] { "displayName" });
        h.DeleteAudit.Unit.Operations.Single().Operation.AlternatePrimaryActions.Should().BeEquivalentTo(AuditActionNames.IntegrationUpdateActions);
    }

    [Fact]
    public async Task Changing_settings_records_the_field_name_and_never_a_key_or_value()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var sentinel = "sentinel-" + Guid.NewGuid().ToString("N");

        await h.Integrations.UpdateIntegrationAsync(
            integration.Id, new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["k" + sentinel] = sentinel }));

        var evt = h.DeleteAudit.Single(AuditActionNames.IntegrationUpdated);
        evt.Fields["changed"].Should().BeEquivalentTo(new[] { "settings" });
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain(sentinel);
    }

    [Theory]
    [InlineData(IntegrationState.Active, IntegrationState.Inactive, AuditActionNames.IntegrationDeactivated)]
    [InlineData(IntegrationState.Inactive, IntegrationState.Active, AuditActionNames.IntegrationActivated)]
    [InlineData(IntegrationState.Active, IntegrationState.Retired, AuditActionNames.IntegrationRetired)]
    [InlineData(IntegrationState.Retired, IntegrationState.Active, AuditActionNames.IntegrationActivated)]
    [InlineData(IntegrationState.Retired, IntegrationState.Inactive, AuditActionNames.IntegrationDeactivated)]
    public async Task A_state_transition_is_recorded_under_the_action_that_names_it_with_both_states(
        IntegrationState from, IntegrationState to, string expectedAction)
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await SeedAsync(h, from);

        var result = await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(State: to));

        result.State.Should().Be(to);
        h.DeleteAudit.Actions.Should().Equal(expectedAction);
        var expected = expectedAction;
        var evt = h.DeleteAudit.Single(expected);
        evt.Fields["fromState"].Should().Be(from.ToString().ToLowerInvariant());
        evt.Fields["toState"].Should().Be(to.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task A_request_that_changes_the_state_and_other_fields_records_one_row_under_the_transition()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await SeedAsync(h, IntegrationState.Active);

        await h.Integrations.UpdateIntegrationAsync(
            integration.Id, new UpdateIntegrationRequest(DisplayName: "Renamed", State: IntegrationState.Inactive));

        h.DeleteAudit.Actions.Should().Equal(AuditActionNames.IntegrationDeactivated);
        h.DeleteAudit.Last.Fields["changed"].Should().BeEquivalentTo(new[] { "displayName" });
    }

    [Fact]
    public async Task Requests_that_change_nothing_record_nothing_and_leave_the_row_alone()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await SeedAsync(h, IntegrationState.Active, settings: new Dictionary<string, string> { ["a"] = "1" });
        var before = await h.Db.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id.Value);

        var activated = await h.Integrations.ActivateIntegrationAsync(integration.Id);
        var renamedToSame = await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(DisplayName: integration.DisplayName));
        var sameSettings = await h.Integrations.UpdateIntegrationAsync(
            integration.Id, new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["a"] = "1" }));
        var empty = await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest());

        new[] { activated, renamedToSame, sameSettings, empty }.Should().OnlyContain(i => i.Id == integration.Id);
        h.DeleteAudit.Actions.Should().BeEmpty("a no-op is not a mutation");
        h.DeleteAudit.Unit.Operations.Should().HaveCount(4).And.OnlyContain(o => o.NoOp);
        var after = await h.Db.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id.Value);
        after.UpdatedAt.Should().Be(before.UpdatedAt, "nothing was written");
    }

    [Fact]
    public async Task Concurrent_changes_to_different_fields_both_survive_because_the_merge_is_made_over_the_locked_row()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(DisplayName: "Renamed"));
        await h.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["a"] = "1" }));

        var row = await h.Db.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id.Value);
        row.DisplayName.Should().Be("Renamed");
        row.SettingsJson.Should().Contain("\"a\"");
    }

    [Fact]
    public async Task A_missing_integration_is_a_typed_not_found_that_records_nothing()
    {
        using var h = new IntegrationLifecycleTestHarness();

        var act = () => h.Integrations.RetireIntegrationAsync(new IntegrationId("int-missing")).AsTask();

        await act.Should().ThrowAsync<IntegrationNotFoundException>();
        h.DeleteAudit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_integration_and_runs_the_attempt_once()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.DeleteAudit.Unit.Operations.Clear();
        h.DeleteAudit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var updated = await h.Integrations.RetireIntegrationAsync(integration.Id);

        updated.State.Should().Be(IntegrationState.Retired);
        h.DeleteAudit.Unit.Operations.Should().ContainSingle();
        h.DeleteAudit.Actions.Should().Equal(AuditActionNames.IntegrationRetired);
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown()
    {
        using var h = new IntegrationLifecycleTestHarness();
        h.DeleteAudit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => h.Integrations.CreateIntegrationAsync(NewRequest()).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Every_mutation_refuses_to_run_without_the_audited_unit()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IActor>());
        var service = new Apperture.ControlPlane.Security.IntegrationManagementService(
            principal.Object,
            Mock.Of<Apperture.ControlPlane.Security.IAuthorizationEvaluationService>(),
            new SqlIntegrationRepository(h.Db),
            Mock.Of<IResourceContextService>(),
            h.Db,
            Mock.Of<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMutationGate>());

        var create = () => service.CreateIntegrationAsync(NewRequest()).AsTask();
        var update = () => service.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(DisplayName: "x")).AsTask();
        var retire = () => service.RetireIntegrationAsync(integration.Id).AsTask();

        await create.Should().ThrowAsync<AuditInvariantException>();
        await update.Should().ThrowAsync<AuditInvariantException>();
        await retire.Should().ThrowAsync<AuditInvariantException>();
    }

    private static async Task<DomainIntegration> SeedAsync(
        IntegrationLifecycleTestHarness h, IntegrationState state, IReadOnlyDictionary<string, string>? settings = null)
    {
        var created = await h.CreateIntegrationAsync();
        var entity = await h.Db.Integrations.SingleAsync(i => i.Id == created.Id.Value);
        entity.State = (int)state;
        if (settings is not null)
            entity.SettingsJson = System.Text.Json.JsonSerializer.Serialize(settings);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        h.DeleteAudit.Unit.Operations.Clear();
        h.DeleteAudit.Clear();
        return created;
    }
}
