using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 4b: the H1 disposition through the real delete path,
/// <see cref="IntegrationManagementService.DeleteIntegrationAsync"/> in a real audited unit on SQL Server, on a locking READ
/// COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog. These show the delete uses the work package 4a primitives
/// correctly (inventory, refusal before change, removal of every override, retirement, evidence) and that its commit, rollback and
/// lost-acknowledgement behaviour are those of one unit. The primitives' own case matrix is in the rest of this class.
/// </summary>
public sealed partial class IntegrationSettingSecretDispositionSqlServerTests
{
    // ---- A to C, J: every override is removed and counted, whatever its definition says ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Deleting_removes_ordinary_undefined_and_reclassified_overrides_by_row_and_counts_them(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        await InsertOverrideAsync(scratch, integration, "Sample.Ordinary", "\"value\"");
        await InsertOverrideAsync(scratch, integration, "Sample.Removed", "\"orphaned\"");
        await InsertOverrideAsync(scratch, integration, "Sample.Reclassified", "42");
        // A historical row whose id does not follow today's "{kind}:{scope}:{key}" shape: removed by its row, not a rebuilt id.
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
            VALUES (N'legacy-{NewSuffix()}', N'Sample.Legacy', N'{PlatformSettingScopeConstants.IntegrationKind}', N'{integration}', N'"x"', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        var registry = new Registry().Ordinary("Sample.Ordinary").PlatformOnlyOrdinary("Sample.Reclassified");

        await DeleteAsync(scratch, integration, registry);

        (await IntegrationExistsAsync(scratch, integration)).Should().BeFalse();
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
        var deleted = (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Single();
        Details(deleted).GetProperty("settingsRemoved").GetInt32().Should().Be(4, "every persisted override, including the undefined and reclassified ones");
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().BeEmpty();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingReset)).Should().BeEmpty("the delete records the removal on its primary row, not per key");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Deleting_retires_an_owned_secret_whose_definition_is_gone_or_reclassified(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var undefined = await CreateSecretAsync(scratch, SettingSecretName("Sample.Gone.Credential"));
        var reclassified = await CreateSecretAsync(scratch, SettingSecretName("Sample.Moved.Credential"));
        await InsertOverrideAsync(scratch, integration, "Sample.Gone.Credential", JsonSerializer.Serialize(undefined.ToString("N")));
        await InsertOverrideAsync(scratch, integration, "Sample.Moved.Credential", JsonSerializer.Serialize(reclassified.ToString("B").ToUpperInvariant()));

        await DeleteAsync(scratch, integration, new Registry().PlatformOnlyOrdinary("Sample.Moved.Credential"));

        (await SecretStateAsync(scratch, undefined)).Should().Be(SecretState.Disabled);
        (await SecretStateAsync(scratch, reclassified)).Should().Be(SecretState.Disabled);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().HaveCount(2);
    }

    // ---- D, K, P: an owned secret, its evidence, nothing secret in it ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Deleting_retires_an_owned_unshared_secret_with_the_primary_and_secrets_rows_in_one_operation(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D").ToUpperInvariant()));
        await InsertOverrideAsync(scratch, integration, "Sample.Ordinary", "\"value\"");
        var (admin, _) = await AdminAsync(scratch);

        await DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey).Ordinary("Sample.Ordinary"), admin);

        (await IntegrationExistsAsync(scratch, integration)).Should().BeFalse();
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);

        var deleted = (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Single();
        deleted["Actor"].Should().Be(admin);
        deleted["Category"].Should().Be(AuditCategories.Configuration);
        deleted["TargetType"].Should().Be(AuditTargetTypes.Integration);
        deleted["TargetId"].Should().Be(integration);
        deleted["ResourceContextId"].Should().Be("shared");
        deleted["ModuleId"].Should().Be("Sample.Mod");
        deleted["OperationEventId"].Should().Be(deleted["EventId"]);
        var details = Details(deleted);
        details.GetProperty("name").GetString().Should().Be("Delete Test Integration");
        details.GetProperty("settingsRemoved").GetInt32().Should().Be(2);
        ((string)deleted["Details"]!).Should().NotContain(CredentialKey, "the primary row names no setting key");

        var retired = (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Single();
        retired["Category"].Should().Be(AuditCategories.Secrets);
        retired["TargetId"].Should().Be($"{integration}:{CredentialKey}");
        retired["OperationEventId"].Should().Be(deleted["OperationEventId"]);
        retired["Actor"].Should().Be(admin);
        Details(retired).GetProperty("reason").GetString().Should().Be("integration_deleted");

        await AssertNoSecretMaterialAsync(scratch, secret);
    }

    // ---- E, F: survivors retain the secret ----

    [Theory]
    [MemberData(nameof(SurvivorsByIsolation))]
    public async Task Deleting_retains_a_secret_another_known_reference_still_holds(bool rcsi, SurvivorKind kind)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var reference = secret.ToString("N").ToUpperInvariant();
        string? otherIntegration = null;
        switch (kind)
        {
            case SurvivorKind.OtherIntegrationSetting:
                otherIntegration = await NewRealIntegrationAsync(scratch);
                await InsertOverrideAsync(scratch, otherIntegration, CredentialKey, JsonSerializer.Serialize(reference));
                break;
            case SurvivorKind.PlatformSetting:
                await InsertSettingAsync(scratch, PlatformSettingScopeConstants.InstanceKind, "inst-" + NewSuffix(), "Sample.Platform", JsonSerializer.Serialize(reference));
                break;
            case SurvivorKind.Connection:
                await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = reference }));
                break;
            default:
                await InsertEntraAsync(scratch, "entra-" + NewSuffix(), secret.ToString("B"));
                break;
        }

        await DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        (await IntegrationExistsAsync(scratch, integration)).Should().BeFalse();
        (await OverrideCountAsync(scratch, integration)).Should().Be(0, "the override is removed even though its secret is kept");
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active, $"a {kind} still holds it");
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().BeEmpty();
        if (otherIntegration is not null)
            (await OverrideCountAsync(scratch, otherIntegration)).Should().Be(1, "another integration's override is never touched");
    }

    // ---- G, H, P: ambiguity refuses the whole delete, before any change ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_undetermined_credential_refuses_the_whole_delete_and_changes_nothing_not_even_the_safe_secret(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var safe = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        var legacy = await CreateSecretAsync(scratch, "legacy-credential");
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(safe.ToString("D")));
        await InsertOverrideAsync(scratch, integration, OtherKey, JsonSerializer.Serialize(legacy.ToString("P")));
        var registry = new Registry().Credential(CredentialKey).Credential(OtherKey);
        var before = await StateAsync(scratch, integration, safe, legacy);

        var act = () => DeleteAsync(scratch, integration, registry);

        var thrown = (await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>()).Which;
        thrown.Refusal.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.OwnershipNotEstablished);
        AssertNoSecretMaterial(thrown, safe.ToString("D"), legacy.ToString("D"), Sentinel);
        thrown.Message.Should().NotContain(OtherKey);
        (await StateAsync(scratch, integration, safe, legacy)).Should().Be(before, "no override removed, no secret retired, no evidence, no audit row");
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_named_for_another_key_refuses_the_delete_even_when_one_of_two_overrides_matches(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertOverrideAsync(scratch, integration, OtherKey, JsonSerializer.Serialize(secret.ToString("N")));
        var before = await StateAsync(scratch, integration, secret);

        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey).Credential(OtherKey));

        (await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>()).Which.Refusal.Reasons
            .Should().Equal(IntegrationSecretAmbiguityReason.SettingKeyMismatch);
        (await StateAsync(scratch, integration, secret)).Should().Be(before);
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
    }

    // ---- I: once-only retirement ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_named_twice_by_its_override_is_retired_once(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(new[] { secret.ToString("D"), secret.ToString("N").ToUpperInvariant() }));

        await DeleteAsync(scratch, integration, new Registry().Ordinary(CredentialKey));

        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().ContainSingle();
    }

    // ---- L, M: any failure rolls back everything ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_audit_insert_failure_rolls_back_the_integration_its_settings_its_secret_and_every_row(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var before = await StateAsync(scratch, integration, secret);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [failAudit]);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        failAudit.Failures.Should().BeGreaterThan(0);
        AssertNoSecretMaterial(thrown, secret.ToString("D"), Sentinel);
        (await StateAsync(scratch, integration, secret)).Should().Be(before);
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_failure_after_the_settings_and_secret_changed_rolls_all_of_it_back(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var before = await StateAsync(scratch, integration, secret);
        // The last statement of the attempt: the overrides are already removed and the secret already disabled in the transaction.
        var failRowDelete = new FailingCommand(sql => sql.Contains("DELETE FROM [Integrations]", StringComparison.Ordinal));

        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [failRowDelete]);

        await act.Should().ThrowAsync<Exception>();
        failRowDelete.Failures.Should().BeGreaterThan(0);
        (await StateAsync(scratch, integration, secret)).Should().Be(before);
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
    }

    // ---- N, O: M6 ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_lost_acknowledgement_after_a_real_commit_is_a_success_with_one_of_everything_and_no_rerun(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        // No exception: the unit verified the commit from this operation's primary row, although the integration is now missing.
        await DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [lost, attempts]);

        lost.Commits.Should().Be(1);
        attempts.Started.Should().Be(1, "a verified commit is never rerun");
        (await IntegrationExistsAsync(scratch, integration)).Should().BeFalse();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().ContainSingle();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().ContainSingle();
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_confirmed_rollback_reruns_once_and_records_one_of_everything(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var attempts = new AttemptCounter();

        await DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [new RolledBackCommit(), attempts]);

        attempts.Started.Should().Be(2);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().ContainSingle();
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_unconfirmed_commit_is_outcome_unknown_and_a_rolled_back_one_never_reads_as_success(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var before = await StateAsync(scratch, integration, secret);
        var verification = new UnreachableVerification();

        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [new RolledBackCommit()], verification: verification);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        verification.Calls.Should().Be(1);
        (await StateAsync(scratch, integration, secret)).Should().Be(before, "the commit was in fact rolled back, and nothing reported it as done");
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_unconfirmed_commit_that_did_commit_is_outcome_unknown_and_the_delete_is_wholly_committed(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var lost = new LostCommitAcknowledgement();
        var verification = new UnreachableVerification();

        // The commit reached the server; the acknowledgement was lost and the outcome cannot be read back.
        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), interceptors: [lost], verification: verification);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>("verification could not establish the outcome, so nothing reports it as done");
        lost.Commits.Should().Be(1);
        verification.Calls.Should().Be(1);
        // Wholly committed, never partly: the integration and its overrides are gone, the one primary row and the one retirement
        // row exist, and the secret is disabled exactly once.
        (await IntegrationExistsAsync(scratch, integration)).Should().BeFalse();
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().ContainSingle();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().ContainSingle();
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);
        await AssertNoSecretMaterialAsync(scratch, secret);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_operation_lock_that_cannot_be_taken_is_outcome_unknown_and_deletes_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var before = await StateAsync(scratch, integration, secret);
        var operationId = Guid.NewGuid().ToString("N");

        // Another session holds this operation's lock, so the unit cannot take it within its wait.
        await using (await HeldLock.TakeAsync(scratch.ConnectionString, "apperture.audit-op." + operationId))
        {
            var act = () => DeleteAsync(
                scratch, integration, new Registry().Credential(CredentialKey), eventIds: new FixedEventIds(operationId));

            (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which.OperationEventId.Should().Be(operationId);
        }

        (await StateAsync(scratch, integration, secret)).Should().Be(before, "no override removed, no secret retired, no evidence, no audit row");
        (await IntegrationExistsAsync(scratch, integration)).Should().BeTrue();
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().BeEmpty();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_integration_another_request_deleted_is_not_found_and_never_a_success(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        await DeleteAsync(scratch, integration, new Registry());

        var again = () => DeleteAsync(scratch, integration, new Registry());

        await again.Should().ThrowAsync<IntegrationNotFoundException>();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().ContainSingle("one primary row, the first delete's");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_integration_removed_after_the_early_check_but_before_the_lock_is_not_found_inside_the_unit(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        // The row disappears between the early read and the locked read: the unit's own read decides, and a missing row there is a
        // genuine "not found", with nothing staged.
        var removeBeforeLock = new BeforeIntegrationLock(() => scratch.ExecuteAsync($"DELETE FROM Integrations WHERE Id = N'{integration}';"));

        var act = () => DeleteAsync(scratch, integration, new Registry(), interceptors: [removeBeforeLock]);

        await act.Should().ThrowAsync<IntegrationNotFoundException>();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().BeEmpty();
    }

    // ---- post-lock authorization ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Authorization_is_decided_again_under_the_lock_and_a_refusal_there_changes_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = await NewRealIntegrationAsync(scratch);
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var before = await StateAsync(scratch, integration, secret);
        var authorization = new DeleteAuthorization { DenyAfter = 1 };

        var act = () => DeleteAsync(scratch, integration, new Registry().Credential(CredentialKey), authorization: authorization);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        authorization.Checks.Should().Be(2, "once before the unit and once against the locked row");
        authorization.Permissions.Should().OnlyContain(p => p == ActionConstants.IntegrationDelete, "deleting never needs integration.update for its settings");
        (await StateAsync(scratch, integration, secret)).Should().Be(before);
    }

    // ---- delete-path harness ----

    public static IEnumerable<object[]> SurvivorsByIsolation() =>
        from rcsi in new[] { false, true }
        from kind in Enum.GetValues<SurvivorKind>()
        select new object[] { rcsi, kind };

    private static async Task<string> NewRealIntegrationAsync(AuditSqlScratch scratch)
    {
        var id = "int-h1d-" + NewSuffix();
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Delete Test Integration', N'Sample.Mod', N'instance', N'shared', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private static async Task<bool> IntegrationExistsAsync(AuditSqlScratch scratch, string integration) =>
        await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Integrations WHERE Id = N'{integration}';") == 1;

    /// <summary>The production delete on a fresh context, with the real audited unit, classifier and retirement on that context.</summary>
    private static async Task DeleteAsync(
        AuditSqlScratch scratch,
        string integration,
        IIntegrationSettingDefinitionRegistry registry,
        string? admin = null,
        IInterceptor[]? interceptors = null,
        IAuditVerificationConnectionFactory? verification = null,
        DeleteAuthorization? authorization = null,
        IAuditEventIdSource? eventIds = null)
    {
        admin ??= (await AdminAsync(scratch)).UserId;
        var principal = new Principal(admin);
        await using var db = scratch.NewDb(interceptors ?? []);
        var store = SecretStore(db);
        var service = new IntegrationManagementService(
            principal,
            authorization ?? new DeleteAuthorization(),
            new SqlIntegrationRepository(db),
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            db,
            new SqlResourceOwnershipMutationGate(db),
            new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, registry),
            new IntegrationSettingSecretRetirement(store),
            NewUnit(db, principal, eventIds: eventIds, verification: verification));
        await service.DeleteIntegrationAsync(new IntegrationId(integration));
    }

    /// <summary>Allows every check, or denies from the check numbered past <see cref="DenyAfter"/>; records what was asked.</summary>
    private sealed class DeleteAuthorization : IAuthorizationEvaluationService
    {
        public int? DenyAfter { get; init; }
        public int Checks { get; private set; }
        public List<string> Permissions { get; } = [];

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            Checks++;
            Permissions.Add(context.Permission.Action);
            return ValueTask.FromResult(DenyAfter is null || Checks <= DenyAfter);
        }
    }

    /// <summary>Runs an action once, just before the delete's locked read of the integration row.</summary>
    private sealed class BeforeIntegrationLock(Func<Task> action) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done && command.CommandText.Contains("[Integrations] WITH (UPDLOCK", StringComparison.Ordinal))
            {
                _done = true;
                await action();
            }

            return result;
        }
    }
}
