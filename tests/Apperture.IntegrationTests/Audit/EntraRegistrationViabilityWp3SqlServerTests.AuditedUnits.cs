using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP3, A10.2b.17 (M6): each audited registration unit that now takes the administrator-set gate keeps the recorder's
// guarantees, and the secret store keeps its transaction (AUD-1.4): an audit-insert failure leaves nothing (no registration, no
// secret); a lost acknowledgement is verified, not rerun; a confirmed rollback reruns the whole unit, which takes the gate again,
// decides again and stores its secret once; an unknown outcome is wholly committed or wholly absent; an operation lock that is not
// granted changes nothing. A refusal produced after the write rolls back the registration, the stored secret and the retirement of
// the old one together, and a failure to record the refusal does not change what the caller sees.
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests
{
    public enum M6Unit
    {
        Create,
        Edit,
        Delete,
        Activate,

        /// <summary>IAM-10.2b WP5: the sign-in policy's unit (gated; a chooser flag cannot reduce either set, so the change is allowed).</summary>
        SignInPolicy,
    }

    public static IEnumerable<object[]> EveryM6UnitOnBothModes() =>
        from unit in Enum.GetValues<M6Unit>()
        from rcsi in new[] { false, true }
        select new object[] { unit, rcsi };

    private sealed class M6Case
    {
        public required Scenario S { get; init; }
        public required Func<AppertureDbContext, IAuditedUnitOfWork, IPlatformAdministratorSetGate?, Task<IActionResult>> Run { get; init; }
        public required Func<Task<bool>> ChangeLanded { get; init; }
        public required Func<Task<int>> RecordedRows { get; init; }

        /// <summary>How many new secrets a committed run leaves (1 for a unit that stores one, else 0).</summary>
        public int NewSecrets { get; init; }
    }

    /// <summary>One allowed change (another administrator can always sign in) that stores or retires a secret where the unit does, ready to run.</summary>
    private async Task<M6Case> ArrangeM6Async(bool rcsi, M6Unit unit)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var secret1 = await StoreSecretAsync(s.World, s.R1, "first-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret1}' WHERE Id = N'{s.R1}';");
        var secret2 = await StoreSecretAsync(s.World, s.R2!, "second-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret2}' WHERE Id = N'{s.R2}';");

        switch (unit)
        {
            case M6Unit.Create:
            {
                var body = EntraRegistrationSqlHarness.CreateBody("M6 created");
                body.ClientSecret = "created-secret";
                return new M6Case
                {
                    S = s,
                    NewSecrets = 1,
                    Run = async (db, u, gate) => (await Tenants(s, db, gate, unit: u).Create(body, CancellationToken.None)).Result!,
                    ChangeLanded = async () => await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE DisplayName = N'M6 created';") == 1,
                    // The audit table is append-only and shared by the cases of the class: count the rows of the registration this case made.
                    RecordedRows = () => s.Scratch.ScalarAsync<int>(
                        $"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.TenantRegistrationCreated}' AND TargetId IN (SELECT Id FROM EntraInstanceRegistrations WHERE DisplayName = N'M6 created');"),
                };
            }

            case M6Unit.Edit:
            {
                var item = await ItemAsync(s, s.R1);
                return new M6Case
                {
                    S = s,
                    NewSecrets = 1,
                    Run = async (db, u, gate) => (await Tenants(s, db, gate, unit: u).Patch(
                        s.R1, Update(item, b => { b.DisplayName = "M6 edited"; b.ClientSecret = "replacement-secret"; }), CancellationToken.None)).Result!,
                    ChangeLanded = async () => await s.Scratch.ScalarAsync<string>($"SELECT DisplayName FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';") == "M6 edited",
                    RecordedRows = () => RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationUpdated, s.R1),
                };
            }

            case M6Unit.Delete:
                return new M6Case
                {
                    S = s,
                    Run = (db, u, gate) => Tenants(s, db, gate, unit: u).Delete(s.R2!, CancellationToken.None),
                    ChangeLanded = async () => await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';") == 0,
                    RecordedRows = () => RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationDeleted, s.R2!),
                };

            case M6Unit.SignInPolicy:
            {
                var current = await CurrentPolicyAsync(s);
                var body = PolicyBody(current, preferEntra: !current.PreferEntraInChooser);
                // The policy is one row and the audit table is shared by the cases of the class: count the rows written since this point.
                var rowsBefore = await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.SignInPolicyUpdated}';");
                return new M6Case
                {
                    S = s,
                    Run = async (db, u, gate) => (await Policy(s, db, gate, unit: u).Patch(body, CancellationToken.None)).Result!,
                    ChangeLanded = async () =>
                        await s.Scratch.ScalarAsync<bool>("SELECT PreferEntraInChooser FROM InstanceAuthUiPolicies;") != current.PreferEntraInChooser,
                    RecordedRows = async () =>
                        await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.SignInPolicyUpdated}';") - rowsBefore,
                };
            }

            default:
                return new M6Case
                {
                    S = s,
                    Run = (db, u, gate) => Tenants(s, db, gate, unit: u).Activate(s.R2!, CancellationToken.None),
                    ChangeLanded = () => s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';"),
                    RecordedRows = () => RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationActivated, s.R2!),
                };
        }
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_audit_insert_failure_leaves_nothing_changed_nothing_recorded_and_no_secret_stored_or_retired(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var before = await StateAsync(c.S.Scratch);

        await using var db = c.S.Scratch.NewDb(failAudit);
        var act = () => c.Run(db, NewUnit(db, c.S.Principal, FastOptions()), null);

        await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0);
        (await c.ChangeLanded()).Should().BeFalse("the change and its audit row commit together, or not at all");
        (await c.RecordedRows()).Should().Be(0);
        (await StateAsync(c.S.Scratch)).Should().Be(before, "neither the registration nor the secret store moved");
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_a_lost_acknowledgement_is_verified_as_committed_and_not_rerun(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var attempts = new AttemptCounter();
        var descriptorsBefore = await SecretDescriptorsAsync(c.S.Scratch);

        await using var db = c.S.Scratch.NewDb(new LostCommitAcknowledgement(), attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        Succeeded(await c.Run(db, NewUnit(db, c.S.Principal, FastOptions()), gate)).Should().BeTrue();

        attempts.Started.Should().Be(1, "verification confirmed the commit, so the decision is not taken again");
        gate.Acquired.Should().Be(1);
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1);
        (await SecretDescriptorsAsync(c.S.Scratch)).Should().Be(descriptorsBefore + c.NewSecrets);
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_a_confirmed_rollback_reruns_the_unit_which_takes_the_gate_and_decides_again_and_stores_its_secret_once(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var attempts = new AttemptCounter();
        var descriptorsBefore = await SecretDescriptorsAsync(c.S.Scratch);

        await using var db = c.S.Scratch.NewDb(new RolledBackCommit(), attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        Succeeded(await c.Run(db, NewUnit(db, c.S.Principal, FastOptions()), gate)).Should().BeTrue();

        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed");
        gate.Acquired.Should().Be(2, "a rerun is a new transaction: the gate it held was released with the rollback and is taken again");
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1, "the rolled-back attempt left no row");
        (await SecretDescriptorsAsync(c.S.Scratch)).Should().Be(descriptorsBefore + c.NewSecrets, "the rolled-back attempt stored no secret of its own");
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_unknown_outcome_is_wholly_committed_or_absent_never_half(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var descriptorsBefore = await SecretDescriptorsAsync(c.S.Scratch);

        await using var db = c.S.Scratch.NewDb(new LostCommitAcknowledgement());
        var act = () => c.Run(db, NewUnit(db, c.S.Principal, FastOptions(), verification: new UnreachableVerification()), null);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        // The commit really happened here, so everything landed; the point is that it is never one part without the other.
        ((await c.ChangeLanded()), (await c.RecordedRows()), await SecretDescriptorsAsync(c.S.Scratch)).Should().Be((true, 1, descriptorsBefore + c.NewSecrets));
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_operation_lock_that_is_not_granted_changes_nothing_and_never_reaches_the_gate(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(c.S.Scratch.ConnectionString, "apperture.audit-op." + operationId);
        var before = await StateAsync(c.S.Scratch);

        await using var db = c.S.Scratch.NewDb();
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var act = () => c.Run(db, NewUnit(db, c.S.Principal, FastOptions(), eventIds: new FixedEventIds(operationId)), gate);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        gate.Acquired.Should().Be(0, "the operation lock comes first: without it the unit does not go on to the gate");
        (await StateAsync(c.S.Scratch)).Should().Be(before);
        (await c.RecordedRows()).Should().Be(0);
    }

    // ---- a refusal produced after the write ----

    private sealed class UnwritableEvents : IAuditEventWriter
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected: the audit store is unavailable.");

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromResult(false);
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M6_a_refused_edit_that_supplied_a_secret_rolls_back_the_new_secret_and_the_retirement_of_the_old_one_and_a_failing_refusal_row_changes_nothing_the_caller_sees(bool rcsi)
    {
        // A depends on R1, which holds a stored secret; B is not present. Disabling R1 with a replacement secret is refused after the
        // secret was stored and the registration updated.
        var s = await ReliantAsync(rcsi);
        var secret1 = await StoreSecretAsync(s.World, s.R1, "first-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret1}' WHERE Id = N'{s.R1}';");
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);
        var events = new UnwritableEvents();

        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db, events: events).Patch(
            s.R1, Update(item, b => { b.Enabled = false; b.ClientSecret = "replacement-secret"; }), CancellationToken.None)).Result!;

        CodeOf(response).Should().Be("last_viable_sign_in_method", "the caller sees the typed refusal, not the failure to record it");
        events.Attempts.Should().Be(1, "the refusal row was attempted once, after the rollback");
        (await StateAsync(s.Scratch)).Should().Be(before, "the provisional registration update, the stored secret and the retirement rolled back together");
        (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secret1}';")).Should().Be((int)SecretState.Active, "the live credential is untouched");
        (await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action IN (N'{AuditActionNames.TenantRegistrationClientSecretStored}', N'{AuditActionNames.TenantRegistrationClientSecretRetired}') AND TargetId = N'{s.R1}';")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refused_delete_retires_nothing_and_a_committed_one_retires_the_registrations_secret_once(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var secret1 = await StoreSecretAsync(s.World, s.R1, "first-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret1}' WHERE Id = N'{s.R1}';");

        await using (var db = s.Scratch.NewDb())
            CodeOf(await Tenants(s, db).Delete(s.R1, CancellationToken.None)).Should().Be("last_viable_sign_in_method");
        (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secret1}';")).Should().Be((int)SecretState.Active, "a refused delete retires nothing");

        // Another administrator can now sign in locally, so the same delete is allowed and retires the secret with it.
        await LocalAdminAsync(s.World, "local");
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R1, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secret1}';")).Should().Be((int)SecretState.Disabled);
        (await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.TenantRegistrationClientSecretRetired}' AND TargetId = N'{s.R1}';")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refusal_is_recorded_once_with_the_operations_existing_action_and_nothing_else_is_written(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var auditBefore = await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using var db = s.Scratch.NewDb();
        await RunAsync(s, RegOp.Delete, db);

        (await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore + 1, "exactly one row: the refusal");
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(1);
    }
}
