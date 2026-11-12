using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5, A10.2b.17 (M6) and A10.2b.15 for the Recovery Access and authenticator units of WP4B-C, composed as the emergency command
// line composes them. The earlier packages ran these obligations for the administrator, registration and policy units only. For each unit: an
// audit-insert failure leaves nothing; a lost acknowledgement is verified, not rerun; a confirmed rollback reruns the whole unit, which takes
// the gate again; an unknown outcome is wholly committed or wholly absent; an operation lock that is not granted changes nothing and never
// reaches the gate; and a gate held past its bound is the typed busy outcome, changes nothing, records nothing, and the retry succeeds.
public sealed partial class AdministratorViabilityPairMatrixSqlServerTests
{
    /// <summary>
    /// The covered audited units of WP4B-C (the reducers and the gated raisers). The command line's password set is an audited unit but not a
    /// covered one: it never changes whether a credential is disabled, so it is out of category and takes no gate (writer inventory).
    /// </summary>
    public enum RecoveryUnit
    {
        Designate,
        Revoke,
        Provision,
        BareReset,
        Enrol,
        Regenerate,
        Switch,
    }

    public static IEnumerable<object[]> EveryRecoveryUnitOnBothModes() =>
        from unit in Enum.GetValues<RecoveryUnit>()
        from rcsi in new[] { false, true }
        select new object[] { unit, rcsi };

    private const string NewPassword = "quiet-maple-lantern-harbor";

    private sealed class UnitCase
    {
        public required World W { get; init; }

        /// <summary>Runs the unit once on the given context and unit of work; a null gate is the production gate at its production bound.</summary>
        public required Func<AppertureDbContext, IAuditedUnitOfWork, IPlatformAdministratorSetGate?, Task> Run { get; init; }

        public required Func<Task<bool>> ChangeLanded { get; init; }

        /// <summary>The unit's own success rows written since the case was arranged.</summary>
        public required Func<Task<int>> RecordedRows { get; init; }
    }

    /// <summary>
    /// One change that is allowed (a spare Entra-independent administrator keeps I-FALLBACK and C-13-R satisfied whatever happens to Recovery
    /// Access), ready to run on a context and unit it is given.
    /// </summary>
    private async Task<UnitCase> ArrangeUnitAsync(bool rcsi, RecoveryUnit unit)
    {
        var w = await WorldAsync(rcsi);
        await w.W.EntraIndependentAdminAsync("spare");

        switch (unit)
        {
            case RecoveryUnit.Designate:
            {
                var target = await w.W.EntraIndependentAdminAsync("target");
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Emergency(db, u, CommandLineCoordinator(db, gate)).DesignateAsync(new UserId(target), Cli),
                    ChangeLanded = () => w.W.DesignatedAsync(target),
                    RecordedRows = () => w.R.AuditRowsAsync(AuditActionNames.RecoveryAccessDesignated, target),
                };
            }

            case RecoveryUnit.Revoke:
            {
                var recovery = await HealthyRecoveryAsync(w);
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Emergency(db, u, CommandLineCoordinator(db, gate)).RevokeAsync(confirmFallback: true, Cli),
                    ChangeLanded = async () => !await w.W.DesignatedAsync(recovery),
                    RecordedRows = () => w.R.AuditRowsAsync(AuditActionNames.RecoveryAccessRevoked, recovery),
                };
            }

            case RecoveryUnit.Provision:
            {
                var rowsBefore = await ActionRowsAsync(w, AuditActionNames.RecoveryAccessProvisioned);
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Emergency(db, u, CommandLineCoordinator(db, gate)).ProvisionAsync(NewPassword, Cli),
                    ChangeLanded = async () => await w.W.DesignatedCountAsync() == 1,
                    RecordedRows = async () => await ActionRowsAsync(w, AuditActionNames.RecoveryAccessProvisioned) - rowsBefore,
                };
            }

            case RecoveryUnit.BareReset:
            {
                var recovery = await HealthyRecoveryAsync(w);
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Mfa(db, u, CommandLineCoordinator(db, gate), w.Clock).ResetAsync(new UserId(recovery), AuditActor.EmergencyCli),
                    ChangeLanded = async () => await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{recovery}';") == 0,
                    RecordedRows = () => w.R.AuditRowsAsync(AuditActionNames.MfaReset, recovery),
                };
            }

            case RecoveryUnit.Enrol:
            {
                var recovery = await UnhealthyRecoveryAsync(w);
                string code;
                await using (var db = w.Scratch.NewDb())
                {
                    var challenge = await w.W.Mfa(db, time: w.Clock).BeginEnrollmentAsync(new UserId(recovery), "account");
                    code = Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), w.Clock.Unix);
                }

                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Mfa(db, u, CommandLineCoordinator(db, gate), w.Clock).CompleteEnrollmentAsync(new UserId(recovery), code, AuditActor.EmergencyCli),
                    ChangeLanded = async () => await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{recovery}' AND IsEnrolled = 1;") == 1,
                    RecordedRows = () => w.R.AuditRowsAsync(AuditActionNames.MfaEnrolled, recovery),
                };
            }

            case RecoveryUnit.Regenerate:
            {
                var recovery = await HealthyRecoveryAsync(w);
                var codesBefore = await CodeIdsAsync(w, recovery);
                var rowsBefore = await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated, recovery);
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Mfa(db, u, CommandLineCoordinator(db, gate), w.Clock).RegenerateRecoveryCodesAsync(new UserId(recovery), AuditActor.EmergencyCli),
                    ChangeLanded = async () => await CodeIdsAsync(w, recovery) != codesBefore,
                    RecordedRows = async () => await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated, recovery) - rowsBefore,
                };
            }

            default:
            {
                var recovery = await HealthyRecoveryAsync(w);
                await StageVerifiedReplacementAsync(w, recovery);
                var (candidateId, version) = w.Candidate!.Value;
                return new UnitCase
                {
                    W = w,
                    Run = async (db, u, gate) => await w.W.Mfa(db, u, CommandLineCoordinator(db, gate), w.Clock)
                        .CompleteReplacementAsync(new UserId(recovery), candidateId, version, AuditActor.EmergencyCli),
                    ChangeLanded = async () => await w.Scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{recovery}';") == candidateId,
                    RecordedRows = () => w.R.AuditRowsAsync(AuditActionNames.MfaReplaced, recovery),
                };
            }
        }
    }

    private static Task<int> ActionRowsAsync(World w, string action) =>
        w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{action}';");

    private static async Task<string> CodeIdsAsync(World w, string userId) =>
        await w.Scratch.ScalarAsync<string?>($"""
            SELECT STRING_AGG(CAST(Id AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM UserMfaRecoveryCodes WHERE UserId = N'{userId}';
            """) ?? "";

    /// <summary>
    /// A stable text of every row one of these units can change, so that "nothing changed" means exactly that. The columns carry different
    /// collations, so each text column is compared in binary.
    /// </summary>
    private static Task<string> UnitStateAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<string>("""
            SELECT CONCAT(
                (SELECT STRING_AGG(CAST(CONCAT(UserId COLLATE Latin1_General_BIN2, '/', RoleId COLLATE Latin1_General_BIN2) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId, RoleId) FROM UserRoles), '|',
                (SELECT STRING_AGG(CAST(CONCAT(UserId COLLATE Latin1_General_BIN2, ':', IsDisabled, ':', PasswordHash COLLATE Latin1_General_BIN2) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId) FROM LocalCredentials), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id COLLATE Latin1_General_BIN2, ':', UserId COLLATE Latin1_General_BIN2, ':', Source) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM IdentityLinks), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id COLLATE Latin1_General_BIN2, ':', IsBreakGlassEligible) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM Users), '|',
                (SELECT STRING_AGG(CAST(CONCAT(UserId COLLATE Latin1_General_BIN2, ':', CredentialId COLLATE Latin1_General_BIN2, ':', IsEnrolled, ':', LastUsedStep) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId) FROM UserTotpCredentials), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id COLLATE Latin1_General_BIN2, ':', ConsumedAtUtc) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM UserMfaRecoveryCodes), '|',
                (SELECT STRING_AGG(CAST(CONCAT(CandidateId COLLATE Latin1_General_BIN2, ':', Status, ':', Version) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY CandidateId) FROM UserTotpReplacementCandidates));
            """);

    private static AdministratorViabilityWp2SqlServerTests.LoggingGate CountingGate(AppertureDbContext db) =>
        new(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U17_an_audit_insert_failure_leaves_nothing_changed_and_nothing_recorded(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var before = await UnitStateAsync(c.W.Scratch);

        await using var db = c.W.Scratch.NewDb(failAudit);
        var act = () => c.Run(db, NewUnit(db, null, FastOptions()), null);

        await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0, "the audit row was attempted");
        (await c.ChangeLanded()).Should().BeFalse("the change and its audit row commit together, or not at all");
        (await c.RecordedRows()).Should().Be(0);
        (await UnitStateAsync(c.W.Scratch)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U17_a_lost_acknowledgement_is_verified_as_committed_and_not_rerun(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);
        var attempts = new AttemptCounter();

        await using var db = c.W.Scratch.NewDb(new LostCommitAcknowledgement(), attempts);
        var gate = CountingGate(db);
        await c.Run(db, NewUnit(db, null, FastOptions()), gate);

        attempts.Started.Should().Be(1, "verification confirmed the commit, so the decision is not taken again");
        gate.Acquired.Should().Be(1);
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U17_a_confirmed_rollback_reruns_the_unit_which_takes_the_gate_and_decides_again(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);
        var attempts = new AttemptCounter();

        await using var db = c.W.Scratch.NewDb(new RolledBackCommit(), attempts);
        var gate = CountingGate(db);
        await c.Run(db, NewUnit(db, null, FastOptions()), gate);

        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed");
        gate.Acquired.Should().Be(2, "a rerun is a new transaction: the gate it held was released with the rollback and is taken again");
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1, "the rolled-back attempt left no row");
    }

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U17_an_unknown_outcome_is_wholly_committed_or_absent_never_half(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);

        await using var db = c.W.Scratch.NewDb(new LostCommitAcknowledgement());
        var act = () => c.Run(db, NewUnit(db, null, FastOptions(), verification: new UnreachableVerification()), null);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>("verification could not establish the outcome, so it is not reported as a success");
        // The commit really happened here, so both landed; the point is that they are never one without the other.
        ((await c.ChangeLanded()), (await c.RecordedRows())).Should().Be((true, 1));
    }

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U17_an_operation_lock_that_is_not_granted_changes_nothing_and_never_reaches_the_gate(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(c.W.Scratch.ConnectionString, "apperture.audit-op." + operationId);
        var before = await UnitStateAsync(c.W.Scratch);

        await using var db = c.W.Scratch.NewDb();
        var gate = CountingGate(db);
        var act = () => c.Run(db, NewUnit(db, null, FastOptions(), eventIds: new FixedEventIds(operationId)), gate);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        gate.Acquired.Should().Be(0, "the operation lock comes first: without it the unit does not go on to the gate");
        (await UnitStateAsync(c.W.Scratch)).Should().Be(before);
        (await c.RecordedRows()).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryRecoveryUnitOnBothModes))]
    public async Task U15_a_gate_held_past_the_bound_is_the_typed_busy_outcome_that_changes_and_records_nothing_and_the_retry_succeeds(RecoveryUnit unit, bool rcsi)
    {
        var c = await ArrangeUnitAsync(rcsi, unit);
        var before = await UnitStateAsync(c.W.Scratch);
        var refusalsBefore = await ActionRowsAsync(c.W, AuditActionNames.AdministratorViabilityBlocked);

        await using (var holder = await HoldTheGateAsync(c.W.Scratch))
        {
            await using var db = c.W.Scratch.NewDb();
            var impatient = new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300));
            var act = () => c.Run(db, NewUnit(db, null, FastOptions()), impatient);

            var busy = (await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which;
            busy.Code.Should().Be("governance_operation_busy");
            busy.Outcome.Should().Be(PlatformAdministratorSetLockOutcome.TimedOut);
        }

        (await UnitStateAsync(c.W.Scratch)).Should().Be(before);
        (await c.RecordedRows()).Should().Be(0, "contention changes nothing and is not recorded as a change");
        (await ActionRowsAsync(c.W, AuditActionNames.AdministratorViabilityBlocked)).Should().Be(refusalsBefore, "contention is not a business refusal");

        await using (var retry = c.W.Scratch.NewDb())
            await c.Run(retry, NewUnit(retry, null, FastOptions()), null);
        (await c.ChangeLanded()).Should().BeTrue("the busy result was transient: the same change succeeds once the gate is free");
        (await c.RecordedRows()).Should().Be(1);
    }
}
