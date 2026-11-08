using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP4B-C: one protected attempt reads the attestation once. Everything below the scope is real (the coordinator, the viability
/// service, the provider, the attested reader and the classifier) over a store and a clock the test controls, so the count is of real
/// reads and the expiry is of the real comparison. The same properties are proven against SQL Server by
/// <c>AdministratorViabilityAttemptSqlServerTests</c>.
/// </summary>
public sealed class AdministratorViabilityAttemptReadingTests
{
    private static readonly DateTime DatabaseNow = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = HostConfigurationAttestationTiming.FreshnessWindow;

    /// <summary>A monotonic clock the test moves by hand.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1_000;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>Hands out snapshots in order (the last repeats) and counts how often it is asked.</summary>
    private sealed class ScriptedStore(params HostConfigurationAttestationSnapshot[] snapshots) : IHostConfigurationAttestationStore
    {
        private int _next;

        public int Reads { get; private set; }

        public Exception? Fail { get; set; }

        public List<string>? Log { get; init; }

        /// <summary>Runs inside every read, so a test can spend time (on its manual clock) while the store is answering.</summary>
        public Action? OnRead { get; init; }

        public ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            Log?.Add("attestation read");
            OnRead?.Invoke();
            if (Fail is not null)
                throw Fail;
            var snapshot = snapshots[Math.Min(_next, snapshots.Length - 1)];
            _next++;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<HostAttestationRefreshOutcome> RefreshAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // The store's writing side belongs to the Management Host's publisher, never to a reader or to the coordinator. The double
        // throws on every writer so that a coordinator path that ever wrote the attestation fails loudly here instead of passing.
        public ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Stored facts a test can change between the two reads of an attempt, as the writer's own mutation does.</summary>
    private sealed class ChangingFacts : IAdministratorFactsReader
    {
        public IReadOnlyList<AdministratorFacts> Administrators { get; set; } = [];

        public RecoveryAccessFacts Recovery { get; set; } = ViabilityTestData.Recovery();

        /// <summary>Runs inside every read of the stored facts, so a test can spend time (on its manual clock) while the state is being read.</summary>
        public Action? OnRead { get; set; }

        public ValueTask<IReadOnlyList<AdministratorFacts>> ReadAdministratorsAsync(CancellationToken cancellationToken = default)
        {
            OnRead?.Invoke();
            return ValueTask.FromResult(Administrators);
        }

        public ValueTask<RecoveryAccessFacts> ReadRecoveryAccessAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Recovery);
    }

    private sealed class LoggingGate(List<string> log) : IPlatformAdministratorSetGate
    {
        public Exception? Fail { get; set; }

        public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            log.Add("gate");
            if (Fail is not null)
                throw Fail;
            return ValueTask.CompletedTask;
        }
    }

    private static HostConfigurationAttestationSnapshot Snapshot(
        TimeSpan age,
        bool localNow = true,
        bool localRestart = true,
        DateTime? stopped = null)
    {
        var payload = AttestedAuthenticationPayload.NotEstablished(localNow, localRestart, new EntraAuthOptions()).Serialize();
        return new HostConfigurationAttestationSnapshot(
            [new HostConfigurationAttestationRow(
                "host", HostConfigurationAttestationSource.ManagementHost, AttestedAuthenticationPayload.SchemaVersion, "process",
                DateTimeOffset.UnixEpoch, DatabaseNow - age, DatabaseNow - age, stopped, "fingerprint", payload)],
            DatabaseNow,
            "fingerprint");
    }

    private static HostConfigurationAttestationSnapshot Missing() => new([], DatabaseNow, "fingerprint");

    private static Mock<IEntraInstanceRegistrationRepository> NoRegistration()
    {
        var registrations = new Mock<IEntraInstanceRegistrationRepository>();
        registrations.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((EntraInstanceRegistration?)null);
        return registrations;
    }

    private static AdministratorViabilityCoordinator Coordinator(
        IHostConfigurationAttestationStore store,
        ChangingFacts facts,
        TimeProvider? clock = null,
        IPlatformAdministratorSetGate? gate = null) =>
        new(
            gate ?? new RecordingGate(),
            new AdministratorViabilityService(
                facts,
                new EffectiveSignInConfigurationProvider(new AttestedDeploymentAuthenticationConfiguration(store), NoRegistration().Object)),
            time: clock);

    private static readonly RecoveryAccessFacts Healthy = new("recovery", true, true, true, true, 3);

    private static readonly RecoveryAccessFacts AfterReset = Healthy with { NativeMfaEnrolled = false, UnusedRecoveryCodes = 0 };

    private static ChangingFacts LocalAdminAndHealthyRecovery() => new()
    {
        Administrators = [Admin("a", local: true), Admin("recovery", local: true, recoveryAccess: true)],
        Recovery = Healthy,
    };

    public enum Verification
    {
        After,
        Designation,
        Revocation,
        BareReset,
        Replacement,
        RaiseOnly,
    }

    private static async ValueTask VerifyAsync(AdministratorViabilityScope scope, Verification verification)
    {
        switch (verification)
        {
            case Verification.After:
                await scope.VerifyAfterAsync();
                break;
            case Verification.Designation:
                await scope.VerifyDesignationAsync();
                break;
            case Verification.Revocation:
                await scope.VerifyRevocationAsync("recovery");
                break;
            case Verification.BareReset:
                await scope.VerifyBareResetAsync("recovery");
                break;
            case Verification.Replacement:
                await scope.VerifyReplacementAsync("recovery");
                break;
            default:
                await scope.AssertRaiseOnlyAsync();
                break;
        }
    }

    // ------------------------------------------------------------------------------- once per attempt, again per attempt

    [Theory]
    [InlineData(Verification.After)]
    [InlineData(Verification.Designation)]
    [InlineData(Verification.Revocation)]
    [InlineData(Verification.BareReset)]
    [InlineData(Verification.Replacement)]
    [InlineData(Verification.RaiseOnly)]
    public async Task One_attempt_reads_the_attestation_exactly_once_whichever_way_it_verifies(Verification verification)
    {
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10)));
        var scope = await Coordinator(store, LocalAdminAndHealthyRecovery()).EnterAsync();

        if (verification == Verification.RaiseOnly)
            await scope.CaptureBeforeForRaiseAsync("a");
        else
            await scope.CaptureBeforeAsync();
        store.Reads.Should().Be(1);

        await VerifyAsync(scope, verification);

        store.Reads.Should().Be(1, "the state after the change is judged under the reading the state before was");
    }

    [Fact]
    public async Task Each_new_attempt_reads_again_and_nothing_is_carried_from_the_last_one()
    {
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10)), Missing(), Snapshot(TimeSpan.FromSeconds(1)));
        var coordinator = Coordinator(store, LocalAdminAndHealthyRecovery());

        var first = await coordinator.EnterAsync();
        await first.CaptureBeforeAsync();
        first.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested);

        var second = await coordinator.EnterAsync();
        await second.CaptureBeforeAsync();
        second.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable, "the evidence is gone by the second attempt, and that attempt must see it");

        var third = await coordinator.EnterAsync();
        await third.CaptureBeforeAsync();
        third.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested, "and the third reads again rather than remembering the refusal");

        store.Reads.Should().Be(3);
    }

    [Fact]
    public async Task Entering_reads_nothing_and_the_evidence_is_read_only_after_the_gate_is_held()
    {
        var log = new List<string>();
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10))) { Log = log };
        var coordinator = Coordinator(store, LocalAdminAndHealthyRecovery(), gate: new LoggingGate(log));

        var scope = await coordinator.EnterAsync();
        store.Reads.Should().Be(0, "an attempt that ends as a no-op before it captures reads no evidence");

        await scope.CaptureBeforeAsync();
        log.Should().Equal("gate", "attestation read");
    }

    [Fact]
    public async Task A_gate_that_is_busy_reads_no_evidence_at_all()
    {
        var log = new List<string>();
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10))) { Log = log };
        var gate = new LoggingGate(log) { Fail = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut) };

        var enter = async () => await Coordinator(store, LocalAdminAndHealthyRecovery(), gate: gate).EnterAsync();

        await enter.Should().ThrowAsync<PlatformAdministratorSetBusyException>();
        store.Reads.Should().Be(0);
    }

    // ------------------------------------------------------------------------------- one consistent interpretation

    [Fact]
    public async Task Evidence_that_goes_away_after_the_reading_cannot_split_one_attempt_into_two_answers()
    {
        // If the second state were read through the attestation, the first would be trusted and the second would not.
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10)), Missing());
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        scope.After!.Trust.Evidence.Should().Be(scope.Captured!.Trust.Evidence);
    }

    [Fact]
    public async Task Evidence_that_appears_after_the_reading_cannot_upgrade_an_attempt_that_began_without_it()
    {
        var store = new ScriptedStore(Missing(), Snapshot(TimeSpan.FromSeconds(1)));
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, "the attempt began with no evidence and stays that way");
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        store.Reads.Should().Be(1);
    }

    [Fact]
    public async Task A_different_reading_after_the_first_cannot_change_what_either_state_says()
    {
        // The host reloads between the reads and says Local is off: the attempt keeps judging under the reading it has.
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10), localNow: true), Snapshot(TimeSpan.FromSeconds(1), localNow: false));
        var scope = await Coordinator(store, LocalAdminAndHealthyRecovery()).EnterAsync();

        await scope.CaptureBeforeAsync();
        await scope.VerifyAfterAsync();

        scope.Captured!.Configuration.Now.LocalSignInEnabled.Should().BeTrue();
        scope.After!.Configuration.Now.LocalSignInEnabled.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------- the freshness edge

    [Fact]
    public async Task At_exactly_the_time_it_had_left_the_reading_still_counts_and_one_tick_later_it_does_not()
    {
        var age = TimeSpan.FromSeconds(170);
        var remaining = Window - age;

        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(age));
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        clock.Advance(remaining);
        (await scope.VerifyBareResetAsync("recovery")).Evidence.Should().Be(ConfigurationEvidence.Attested, "exactly the window is still fresh, as the classifier says");

        var clockLate = new ManualClock();
        var storeLate = new ScriptedStore(Snapshot(age));
        var factsLate = LocalAdminAndHealthyRecovery();
        var scopeLate = await Coordinator(storeLate, factsLate, clockLate).EnterAsync();
        await scopeLate.CaptureBeforeAsync();
        factsLate.Recovery = AfterReset;

        clockLate.Advance(remaining + TimeSpan.FromTicks(1));
        var late = await scopeLate.VerifyBareResetAsync("recovery");

        late.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, "holding the reading must not outlast the window it had");
        late.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        late.Verdict.Detail.Should().Contain("went out of date");
        storeLate.Reads.Should().Be(1, "running out is judged on the reading it has, not by reading again");
    }

    // ------------------------------------------------------------------------------- where the attempt's time is spent
    //
    // The tests above move the clock between the calls. These spend time INSIDE the two reads an attempt waits for: the attestation
    // store's answer, and the read of the state after the change. Each double advances the manual clock from within the operation, so
    // an ordering mistake (the clock started after the store answers, expiry judged before the later read) shows as a different answer.
    // Nothing here sleeps or reads the system clock.

    /// <summary>What the attestation has left when the attempt reads it: a row 170 seconds old in a 180-second window.</summary>
    private static readonly TimeSpan Left = TimeSpan.FromSeconds(10);

    private static ConfigurationEvidence?[] EvidenceOfBothStates(AdministratorViabilityScope scope) =>
        [scope.Captured!.Trust.Evidence, scope.After!.Trust.Evidence];

    private static void BothStatesLostTheEvidence(AdministratorViabilityScope scope)
    {
        EvidenceOfBothStates(scope).Should().Equal(
            [ConfigurationEvidence.Unavailable, ConfigurationEvidence.Unavailable],
            "the captured state and the state after the change are classified together");
        foreach (var state in new[] { scope.Captured!, scope.After! })
        {
            state.Trust.Local.Should().Be(ConfigurationTrustLevel.Unknown);
            state.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        }
    }

    private static void BothStatesKeptTheEvidence(AdministratorViabilityScope scope) =>
        EvidenceOfBothStates(scope).Should().Equal(
            [ConfigurationEvidence.Attested, ConfigurationEvidence.Attested],
            "the captured state and the state after the change are classified together");

    /// <summary>Arms the facts reader to spend <paramref name="by"/> on the clock during the next read of the state, once.</summary>
    private static void SpendDuringNextStateRead(ChangingFacts facts, ManualClock clock, TimeSpan by) =>
        facts.OnRead = () =>
        {
            facts.OnRead = null;
            clock.Advance(by);
        };

    [Theory]
    [InlineData(Verification.After)]
    [InlineData(Verification.Designation)]
    [InlineData(Verification.Revocation)]
    [InlineData(Verification.BareReset)]
    [InlineData(Verification.Replacement)]
    public async Task Time_spent_waiting_for_the_attestation_store_counts_against_the_evidence_it_returns(Verification verification)
    {
        // The whole of the time is spent inside the store's answer, and not one tick after it. An attempt that starts its clock when
        // the answer arrives would measure none of it and would rely on a row that was already out of date.
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left)) { OnRead = () => clock.Advance(Left + TimeSpan.FromTicks(1)) };
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        await VerifyAsync(scope, verification);

        BothStatesLostTheEvidence(scope);
        store.Reads.Should().Be(1, "running out is judged on the reading the attempt has; it is never a reason to read again");
    }

    [Fact]
    public async Task Time_spent_inside_the_attestation_store_up_to_exactly_what_was_left_does_not_expire_the_evidence()
    {
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left)) { OnRead = () => clock.Advance(Left) };
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.Evidence.Should().Be(ConfigurationEvidence.Attested, "exactly what was left is still fresh");
        BothStatesKeptTheEvidence(scope);
        store.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(Verification.After)]
    [InlineData(Verification.Designation)]
    [InlineData(Verification.Revocation)]
    [InlineData(Verification.BareReset)]
    [InlineData(Verification.Replacement)]
    public async Task Time_spent_reading_the_state_after_the_change_counts_against_the_evidence_the_attempt_holds(Verification verification)
    {
        // The reading is taken at once and fresh. The time passes while the state AFTER the change is being read, and nowhere else.
        // Expiry has to be judged once that read is done: judged before it, the attempt would decide on evidence that had run out
        // while it was reading.
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left));
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;
        SpendDuringNextStateRead(facts, clock, Left + TimeSpan.FromTicks(1));

        await VerifyAsync(scope, verification);

        BothStatesLostTheEvidence(scope);
        store.Reads.Should().Be(1, "the state after the change is judged under the reading the attempt already has");
    }

    [Fact]
    public async Task Time_spent_reading_the_state_after_the_change_up_to_exactly_what_was_left_does_not_expire_the_evidence()
    {
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left));
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;
        SpendDuringNextStateRead(facts, clock, Left);

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.Evidence.Should().Be(ConfigurationEvidence.Attested, "exactly what was left is still fresh");
        BothStatesKeptTheEvidence(scope);
        store.Reads.Should().Be(1);
    }

    [Fact]
    public async Task The_time_of_the_store_and_of_the_later_state_read_add_up_and_the_decision_refuses_as_unavailable()
    {
        // Neither read alone uses more than the 10 seconds that were left (6 and 5); together they use 11.
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left)) { OnRead = () => clock.Advance(TimeSpan.FromSeconds(6)) };
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;
        SpendDuringNextStateRead(facts, clock, TimeSpan.FromSeconds(5));

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.IsAllowed.Should().BeFalse();
        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable, "required evidence that ran out is unavailable, whichever read spent the time");
        decision.Verdict.Detail.Should().Contain("went out of date");
        BothStatesLostTheEvidence(scope);
        store.Reads.Should().Be(1);
    }

    [Fact]
    public async Task The_time_of_the_store_and_of_the_later_state_read_together_up_to_exactly_what_was_left_still_count()
    {
        var clock = new ManualClock();
        var store = new ScriptedStore(Snapshot(Window - Left)) { OnRead = () => clock.Advance(TimeSpan.FromSeconds(6)) };
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;
        SpendDuringNextStateRead(facts, clock, TimeSpan.FromSeconds(4));

        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        BothStatesKeptTheEvidence(scope);
        store.Reads.Should().Be(1);
    }

    [Fact]
    public async Task When_the_reading_runs_out_both_states_lose_its_trust_together_so_they_cannot_disagree()
    {
        var clock = new ManualClock();
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(new ScriptedStore(Snapshot(TimeSpan.FromSeconds(170))), facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        scope.Captured!.Trust.LocalKnown.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(11));
        await scope.VerifyAfterAsync();

        foreach (var state in new[] { scope.Captured!, scope.After! })
        {
            state.Trust.Local.Should().Be(ConfigurationTrustLevel.Unknown);
            state.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
            state.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
            state.Configuration.Trust.Should().BeSameAs(state.Trust);
        }
    }

    [Fact]
    public async Task A_decision_that_needs_no_reading_is_not_stopped_by_the_reading_running_out()
    {
        var clock = new ManualClock();
        var facts = new ChangingFacts
        {
            Administrators = [Admin("recovery", local: true, recoveryAccess: true)],
            Recovery = Healthy with { UnusedRecoveryCodes = 0 },
        };
        var scope = await Coordinator(new ScriptedStore(Snapshot(TimeSpan.FromSeconds(170))), facts, clock).EnterAsync();
        await scope.CaptureBeforeAsync();
        facts.Recovery = AfterReset;

        clock.Advance(TimeSpan.FromMinutes(5));
        var decision = await scope.VerifyBareResetAsync("recovery");

        decision.IsAllowed.Should().BeTrue("the posture was not healthy, so a reset cannot have reduced anything");
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
    }

    [Fact]
    public async Task A_raise_only_writer_is_not_refused_when_the_reading_runs_out()
    {
        var clock = new ManualClock();
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(new ScriptedStore(Snapshot(TimeSpan.FromSeconds(170))), facts, clock).EnterAsync();
        await scope.CaptureBeforeForRaiseAsync("a");

        clock.Advance(TimeSpan.FromMinutes(5));
        var assert = async () => await scope.AssertRaiseOnlyAsync();

        await assert.Should().NotThrowAsync("a raise-only assertion needs no trusted configuration");
    }

    [Fact]
    public void The_classifier_says_how_much_of_the_window_is_left_by_the_databases_clock()
    {
        HostConfigurationAttestationClassifier.Assess(Snapshot(TimeSpan.FromSeconds(170)), Window).Reading!.EvidenceRemaining
            .Should().Be(TimeSpan.FromSeconds(10));
        HostConfigurationAttestationClassifier.Assess(Snapshot(Window), Window).Reading!.EvidenceRemaining
            .Should().Be(TimeSpan.Zero, "an age equal to the window is fresh with nothing left");
        HostConfigurationAttestationClassifier.Assess(Snapshot(Window + TimeSpan.FromTicks(1)), Window).Reading
            .Should().BeNull("a stale record gives no reading");
    }

    // ------------------------------------------------------------------------------- the attempt's reading on its own

    private sealed class FixedDeployment(DeploymentAuthenticationReading reading) : IDeploymentAuthenticationConfiguration
    {
        public int Reads { get; private set; }

        public DeploymentAuthenticationReading Read()
        {
            Reads++;
            return reading;
        }
    }

    private static DeploymentAuthenticationReading HostReading() =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], true, new EntraAuthOptions(), AuthoritativeAboutSecrets: true, Running: null);

    [Fact]
    public async Task A_reading_of_a_host_about_itself_has_no_window_and_never_runs_out_but_is_still_read_once()
    {
        var clock = new ManualClock();
        var deployment = new FixedDeployment(HostReading());
        var attempt = new AttemptConfigurationReading(clock);

        var first = await attempt.GetAsync(deployment);
        clock.Advance(TimeSpan.FromDays(1));
        var second = await attempt.GetAsync(deployment);

        second.Should().BeSameAs(first);
        deployment.Reads.Should().Be(1);
        attempt.HasExpired.Should().BeFalse();
    }

    [Fact]
    public async Task An_attested_reading_that_does_not_say_how_long_it_has_left_counts_as_having_run_out()
    {
        var attempt = new AttemptConfigurationReading(new ManualClock());

        await attempt.GetAsync(new FixedDeployment(HostReading() with { Evidence = ConfigurationEvidence.Attested, EvidenceRemaining = null }));

        attempt.HasExpired.Should().BeTrue("evidence with no stated limit is not given an unlimited one");
    }

    [Fact]
    public void Nothing_read_yet_has_not_run_out() =>
        new AttemptConfigurationReading(new ManualClock()).HasExpired.Should().BeFalse();

    [Fact]
    public async Task A_read_that_failed_is_not_retried_inside_the_attempt_and_the_next_attempt_reads_again()
    {
        var failure = new TimeoutException();
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10))) { Fail = failure };
        var coordinator = Coordinator(store, LocalAdminAndHealthyRecovery());
        var scope = await coordinator.EnterAsync();

        var capture = async () => await scope.CaptureBeforeAsync();
        (await capture.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(failure, "a failure to read is not turned into a reading");

        store.Fail = null;
        await capture.Should().ThrowAsync<InvalidOperationException>("the attempt has had its one read");
        store.Reads.Should().Be(1);
        scope.Captured.Should().BeNull();

        var next = await coordinator.EnterAsync();
        await next.CaptureBeforeAsync();
        next.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested);
        store.Reads.Should().Be(2);
    }

    // ------------------------------------------------------------------------------- nothing else can make a second reading

    private static IReadOnlyList<string> ProductionFilesContaining(string text) =>
        SourceTree.Files("src", [".cs"], includeMigrations: false, SourceTree.Floor.CSharpOutsideMigrations)
            .Where(file => File.ReadAllText(file).Contains(text, StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void A_reading_is_made_only_by_a_scope_and_read_through_only_by_the_layers_under_it()
    {
        ProductionFilesContaining("new AttemptConfigurationReading(").Should().Equal("AdministratorViabilityCoordinator.cs");
        ProductionFilesContaining("ReadForAttemptAsync(").Should().Equal(
            "AdministratorViabilityCoordinator.cs",
            "AdministratorViabilityService.cs",
            "EffectiveSignInConfigurationProvider.cs");
    }

    [Fact]
    public void The_replacements_refusal_is_produced_only_by_the_replacement_decision_and_never_by_the_net_evaluation_the_entra_primary_policy_asks_for()
    {
        var viability = Path.Combine(RepositoryRoot.Find(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "AdministratorViability");
        var guard = File.ReadAllText(Path.Combine(viability, "AdministratorViabilityGuard.cs"));

        var produced = System.Text.RegularExpressions.Regex.Matches(guard, @"new\s+ViabilityVerdict\(\s*ViabilityVerdictKind\.RecoveryPostureReduced");
        produced.Should().ContainSingle("only one place builds the verdict");
        produced[0].Index.Should().BeGreaterThan(guard.IndexOf("public static ViabilityDecision DecideReplacement(", StringComparison.Ordinal), "and it is the replacement's decision");
        guard.IndexOf("public static ViabilityVerdict Evaluate(", StringComparison.Ordinal).Should().BeGreaterThan(-1);

        var policy = File.ReadAllText(Path.Combine(viability, "..", "EntraPrimaryAuthenticationPolicy.cs"));
        policy.Should().NotContain("DecideReplacement", "the policy judges a disabled credential through the net evaluation, which cannot return the replacement's refusal");
        policy.Should().NotContain("VerifyReplacementAsync");
    }

    [Fact]
    public void The_scope_has_no_way_to_read_the_state_except_through_its_attempt()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "AdministratorViability", "AdministratorViabilityCoordinator.cs"));

        source.Should().Contain("_reads.ReadForAttemptAsync(_attempt,");
        source.Should().NotContain("_reads.ReadAsync(", "that read would go to the attestation a second time");
        source.Should().NotContain("_deployment.ReadAsync(", "the coordinator builds through the deployment and never reads through it");
    }

    [Fact]
    public async Task The_reading_is_a_read_of_the_configuration_only_and_the_stored_facts_are_read_again_after_the_change()
    {
        // The whole point of the second read: pinning the configuration must not pin what the mutation changed.
        var store = new ScriptedStore(Snapshot(TimeSpan.FromSeconds(10)));
        var facts = LocalAdminAndHealthyRecovery();
        var scope = await Coordinator(store, facts).EnterAsync();
        await scope.CaptureBeforeAsync();

        facts.Recovery = AfterReset;
        await scope.VerifyAfterAsync();

        scope.Captured!.RecoveryHealthy.Should().BeTrue();
        scope.After!.RecoveryHealthy.Should().BeFalse();
    }
}
