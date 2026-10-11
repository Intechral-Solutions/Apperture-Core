using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Models;
using Apperture.UnitTests.Audit;
using Apperture.UnitTests.Integration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// AUD-1.4: which audit rows the platform and integration settings services decide to record. The transactional guarantees
/// (rollback, atomic secret changes, verification) are proven against SQL Server in SettingsAuditSqlServerTests.
/// </summary>
public sealed class SettingsAuditTests
{
    private static readonly string SmtpHost = PlatformSettingKeys.NotificationsEmailSmtpHost;

    // ---- Platform: ordinary settings ----

    [Fact]
    public async Task Setting_an_ordinary_value_records_the_change_on_the_setting_with_the_units_actor()
    {
        var (sut, repo, recorded) = Platform();

        await sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.one.test" }, null);
        var first = (await repo.GetByKeyAsync(SmtpHost))!;
        await sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.two.test" }, Convert.ToBase64String([1]));

        recorded.Actions.Should().Equal("platform.settings.set", "platform.settings.set");
        var firstEvent = recorded.Events[0].Event;
        firstEvent.Target!.Type.Should().Be(AuditTargetTypes.PlatformSetting);
        firstEvent.Target.Id.Should().Be(SmtpHost);
        firstEvent.Fields["operation"].Should().Be("set");
        firstEvent.Fields["sensitive"].Should().Be(false);
        firstEvent.Fields["newValue"].Should().Be("smtp.one.test");
        firstEvent.Fields.Should().NotContainKey("previousValue");
        recorded.Events[1].Event.Fields["previousValue"].Should().Be("smtp.one.test");
        recorded.Events[1].Event.Fields["newValue"].Should().Be("smtp.two.test");

        // The row and the audit row share the actor by construction: it is the unit's, never a caller's.
        first.UpdatedByPrincipalId.Should().Be(recorded.Unit.CurrentPrincipalActor.RecordedValue);
    }

    [Fact]
    public async Task Setting_the_value_an_override_already_holds_changes_nothing_and_records_nothing()
    {
        var (sut, repo, recorded) = Platform(seed: Row(SmtpHost, "smtp.same.test", rowVersion: [1]));

        var result = await sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.same.test" }, Convert.ToBase64String([1]));

        result.CurrentValue.Should().Be("smtp.same.test");
        recorded.Actions.Should().BeEmpty();
        (await repo.GetByKeyAsync(SmtpHost))!.RowVersion.Should().Equal([1], "nothing was written");
    }

    [Fact]
    public async Task A_stale_row_version_is_a_conflict_even_when_the_value_would_have_been_the_same()
    {
        var (sut, _, recorded) = Platform(seed: Row(SmtpHost, "smtp.same.test", rowVersion: [1]));

        var act = () => sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.same.test" }, Convert.ToBase64String([2]));

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>();
        recorded.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Resetting_records_the_previous_ordinary_value_and_resetting_an_absent_override_records_nothing()
    {
        var (sut, repo, recorded) = Platform(seed: Row(SmtpHost, "smtp.gone.test", rowVersion: [1]));

        await sut.ResetAsync(SmtpHost);
        await sut.ResetAsync(SmtpHost);

        recorded.Actions.Should().Equal("platform.settings.reset");
        var reset = recorded.Single("platform.settings.reset");
        reset.Fields["previousValue"].Should().Be("smtp.gone.test");
        reset.Fields["sensitive"].Should().Be(false);
        (await repo.GetByKeyAsync(SmtpHost)).Should().BeNull();
    }

    [Fact]
    public async Task A_lost_acknowledgement_reports_the_committed_change_without_running_the_change_twice()
    {
        var (sut, repo, recorded) = Platform();
        recorded.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var result = await sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.lost.test" }, null);

        result.CurrentValue.Should().Be("smtp.lost.test");
        recorded.Actions.Should().Equal("platform.settings.set");
        recorded.Unit.Operations.Should().ContainSingle("the attempt ran once and the result came from the committed row");
        (await repo.GetByKeyAsync(SmtpHost)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown_and_records_nothing()
    {
        var (sut, _, recorded) = Platform();
        recorded.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "smtp.unknown.test" }, null);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        recorded.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_audited_unit_a_change_is_refused_as_an_audit_invariant_not_as_a_validation_error()
    {
        var def = new Apperture.Core.Application.PlatformSettings.PlatformSettingDefinitionRegistry().TryGet(SmtpHost)!;
        var repo = new PlatformSettingRepositoryFake();
        var sut = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo,
            Resolver(repo),
            PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object),
            new PlatformSettingSecretMaterialProvisioner(new InMemorySecretStore()));

        var act = () => sut.SetAsync(SmtpHost, new PlatformSettingUpdatePayload { ScalarValue = "x" }, null);

        // Controllers map InvalidOperationException to 400 but let this one through as the internal error it is.
        await act.Should().ThrowAsync<AuditInvariantException>();
        (await repo.GetByKeyAsync(SmtpHost)).Should().BeNull();
    }

    // ---- Producers ----

    [Fact]
    public void A_sensitive_setting_event_cannot_carry_a_value()
    {
        var setPlatform = () => AuditEvents.PlatformSettingSet("some.key", "replace", sensitive: true, newValue: "hunter2");
        var setIntegration = () => AuditEvents.IntegrationSettingSet("int-1", "some.key", "replace", sensitive: true, previousValue: "hunter2");
        var resetPlatform = () => AuditEvents.PlatformSettingReset("some.key", sensitive: true, previousValue: "hunter2");
        var resetIntegration = () => AuditEvents.IntegrationSettingReset("int-1", "some.key", sensitive: true, previousValue: "hunter2");

        setPlatform.Should().Throw<ArgumentException>();
        setIntegration.Should().Throw<ArgumentException>();
        resetPlatform.Should().Throw<ArgumentException>();
        resetIntegration.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Setting_secret_events_target_the_setting_and_carry_closed_values_only()
    {
        var platform = AuditEvents.PlatformSettingSecretStored("some.key", "replaced");
        var integration = AuditEvents.IntegrationSettingSecretRetired("int-1", "some.key", "cleared", resourceContextId: "ctx-1");

        platform.Definition.Category.Should().Be(AuditCategories.Secrets);
        platform.Target!.Type.Should().Be(AuditTargetTypes.PlatformSetting);
        platform.Fields.Keys.Should().BeEquivalentTo("key", "change");
        integration.Target!.Type.Should().Be(AuditTargetTypes.IntegrationSetting);
        integration.Target.Id.Should().Be(AuditEvents.IntegrationSettingTargetId("int-1", "some.key"));
        integration.Fields.Keys.Should().BeEquivalentTo("integrationId", "key", "reason");
        integration.Related.ResourceContextId.Should().Be("ctx-1");
    }

    // ---- Integration: the in-transaction reset ----

    [Fact]
    public async Task The_in_transaction_reset_joins_the_callers_unit_and_stages_secondary_rows_without_opening_one()
    {
        using var harness = new IntegrationLifecycleTestHarness();
        var integration = await harness.CreateIntegrationAsync();
        await harness.Settings.SetAsync(
            integration.Id.Value,
            harness.SensitiveSettingKey,
            new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = "to-be-removed" },
            null);
        await harness.Settings.SetAsync(
            integration.Id.Value,
            harness.SettingKey,
            new PlatformSettingUpdatePayload { ScalarValue = "plain" },
            null);
        var secretId = harness.LastProvisionedSecretId;
        harness.SettingsAudit.Clear();
        var operationsBefore = harness.SettingsAudit.Unit.Operations.Count;

        bool secretReset = false, plainReset = false;
        await harness.SettingsAudit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationSettingSet, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Stands in for the caller's own primary row.
                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSet(integration.Id.Value, "caller.key", "set", sensitive: false, newValue: "x"), ct);
                secretReset = await harness.ResetInTransaction.ResetInTransactionAsync(transaction, integration.Id.Value, harness.SensitiveSettingKey, ct);
                plainReset = await harness.ResetInTransaction.ResetInTransactionAsync(transaction, integration.Id.Value, harness.SettingKey, ct);
                return true;
            },
            (_, _) => Task.FromResult(true));

        secretReset.Should().BeTrue();
        plainReset.Should().BeTrue();
        harness.SettingsAudit.Unit.Operations.Should().HaveCount(operationsBefore + 1, "the helper opened no unit of its own");
        harness.SettingsAudit.Actions.Should().Equal(
            "integration.settings.set",
            "integration.settings.reset",
            "integration.settings.secret.retired",
            "integration.settings.reset");
        harness.SecretStore.Get(secretId.Value.ToString("D"))!.State.Should().Be(SecretState.Disabled);
        harness.Db.PlatformSettingValues.Should().NotContain(v => v.ScopeId == integration.Id.Value);
        foreach (var (auditEvent, _) in harness.SettingsAudit.Events)
        {
            string.Join("|", auditEvent.Fields.Values.Select(v => v?.ToString())).Should().NotContain("to-be-removed")
                .And.NotContain(secretId.Value.ToString("D"));
        }
    }

    [Fact]
    public async Task The_in_transaction_reset_changes_and_stages_nothing_when_there_is_no_override()
    {
        using var harness = new IntegrationLifecycleTestHarness();
        var integration = await harness.CreateIntegrationAsync();
        harness.SettingsAudit.Clear();

        bool reset = true;
        await harness.SettingsAudit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationSettingSet, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSet(integration.Id.Value, "caller.key", "set", sensitive: false, newValue: "x"), ct);
                reset = await harness.ResetInTransaction.ResetInTransactionAsync(transaction, integration.Id.Value, harness.SettingKey, ct);
                return true;
            },
            (_, _) => Task.FromResult(true));

        reset.Should().BeFalse();
        harness.SettingsAudit.Actions.Should().Equal("integration.settings.set");
    }

    [Fact]
    public async Task The_in_transaction_reset_is_registered_for_the_services_assembly_only()
    {
        // The interface is internal: no controller or other assembly can resolve or call it, so it is not a way to reset a
        // setting from a request. Only the services registration exposes it.
        typeof(IIntegrationSettingResetInTransaction).IsNotPublic.Should().BeTrue();
        typeof(IIntegrationSettingsService).GetMethods().Select(m => m.Name).Should().NotContain("ResetInTransactionAsync");
        await Task.CompletedTask;
    }

    // ---- helpers ----

    private static (PlatformSettingsService Service, PlatformSettingRepositoryFake Repository, AuditRecording Recorded) Platform(
        PlatformSettingValue? seed = null)
    {
        var def = new Apperture.Core.Application.PlatformSettings.PlatformSettingDefinitionRegistry().TryGet(SmtpHost)!;
        var repo = seed is null ? new PlatformSettingRepositoryFake() : new PlatformSettingRepositoryFake(seed);
        var recorded = new AuditRecording();
        var service = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo,
            Resolver(repo),
            PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object),
            new PlatformSettingSecretMaterialProvisioner(new InMemorySecretStore()),
            recorded);
        return (service, repo, recorded);
    }

    private static IPlatformSettingsResolver Resolver(IPlatformSettingValueRepository repo) =>
        new PlatformSettingsResolver(
            new Apperture.Core.Application.PlatformSettings.PlatformSettingDefinitionRegistry(),
            repo,
            new ConfigurationBuilder().Build());

    private static PlatformSettingValue Row(string key, string value, byte[] rowVersion)
    {
        var def = new Apperture.Core.Application.PlatformSettings.PlatformSettingDefinitionRegistry().TryGet(key)!;
        return new PlatformSettingValue
        {
            Key = key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, value),
            RowVersion = rowVersion
        };
    }
}
