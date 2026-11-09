using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Accounts;
using Apperture.UnitTests.AdministratorViability;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed partial class NativeMfaServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enrollment_requires_a_valid_totp_before_becoming_active()
    {
        var harness = Harness.Create();
        var userId = new UserId("usr-e");

        var challenge = await harness.Mfa.BeginEnrollmentAsync(userId, "emergency");
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();

        var act = () => harness.Mfa.CompleteEnrollmentAsync(userId, "000000", AuditActor.EmergencyCli).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();
        harness.Recorder.Staged.Should().BeEmpty("a refused code commits nothing");

        var secret = Base32.Decode(challenge.ManualEntryKey);
        var code = Rfc6238Totp.Compute(secret, Now.ToUnixTimeSeconds());
        var recoveryCodes = await harness.Mfa.CompleteEnrollmentAsync(userId, code, AuditActor.EmergencyCli);

        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeTrue();
        recoveryCodes.Should().HaveCount(10);
        harness.Totp.LastProtectedSecret.Should().NotBeNull();
        Convert.ToBase64String(harness.Totp.LastProtectedSecret!).Should().NotBe(Convert.ToBase64String(secret));
        harness.Recovery.PlaintextNeverStored.Should().BeTrue();

        // AUD-1.2: the command line is the actor, the user is the target (the earlier rows named the user as actor).
        var operation = harness.Recorder.Operations.Last();
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        operation.Staged.Select(e => e.Action).Should().Equal(AuditActionNames.MfaEnrolled, AuditActionNames.RecoveryCodesGenerated);
        operation.Staged.Should().OnlyContain(e => e.Target!.Id == userId.Value);
        harness.Recorder.Single(AuditActionNames.RecoveryCodesGenerated).Fields["count"].Should().Be(10);
        harness.RecordedText().Should().NotContain(code).And.NotContain(recoveryCodes);
    }

    [Fact]
    public async Task Beginning_an_enrollment_over_an_enrolled_authenticator_is_refused_and_the_enrollment_stays()
    {
        var harness = await Harness.EnrolledAsync();
        var stored = harness.Totp.LastProtectedSecret;

        var act = () => harness.Mfa.BeginEnrollmentAsync(harness.UserId, "emergency").AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already enrolled");
        (await harness.Mfa.IsEnrolledAsync(harness.UserId)).Should().BeTrue();
        harness.Totp.LastProtectedSecret.Should().BeSameAs(stored, "nothing was written");
    }

    [Fact]
    public async Task A_pending_write_that_the_store_refuses_is_reported_as_already_enrolled_and_returns_no_secret()
    {
        // The advisory check passes (nothing is enrolled when it is made); the write finds an enrolment that completed in between.
        var harness = Harness.Create();
        harness.Totp.RefuseNextPendingWrite = true;

        var act = () => harness.Mfa.BeginEnrollmentAsync(new UserId("usr-race"), "emergency").AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already enrolled");
        harness.Totp.LastProtectedSecret.Should().BeNull("the generated secret was not stored");
        harness.Recorder.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task An_enrollment_whose_pending_secret_was_replaced_after_its_code_was_checked_fails_and_enrolls_nothing()
    {
        // The code is valid for the secret the enrollment read. A begin then replaces the pending secret, so the conditional write finds
        // another credential and reports it; the enrollment must not enroll a secret nobody verified.
        var harness = Harness.Create();
        var userId = new UserId("usr-stale");
        var challenge = await harness.Mfa.BeginEnrollmentAsync(userId, "emergency");
        var code = Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), Now.ToUnixTimeSeconds());
        harness.Totp.BeforeNextEnrol = () => harness.Mfa.BeginEnrollmentAsync(userId, "emergency").AsTask().GetAwaiter().GetResult();

        var act = () => harness.Mfa.CompleteEnrollmentAsync(userId, code, AuditActor.EmergencyCli).AsTask();

        var conflict = (await act.Should().ThrowAsync<GovernanceConflictException>()).Which;
        conflict.Code.Should().Be(GovernanceConflictCodes.AuthenticatorChanged, "a typed, retryable conflict: start the enrollment again");
        conflict.Message.Should().Contain("changed while the code was being checked");
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();
        harness.Recorder.Staged.Should().BeEmpty("nothing was committed");
    }

    [Fact]
    public async Task Beginning_an_enrollment_again_while_it_is_pending_replaces_the_pending_secret()
    {
        var harness = Harness.Create();
        var userId = new UserId("usr-retry");

        var first = await harness.Mfa.BeginEnrollmentAsync(userId, "emergency");
        var second = await harness.Mfa.BeginEnrollmentAsync(userId, "emergency");

        second.ManualEntryKey.Should().NotBe(first.ManualEntryKey);
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_removes_the_authenticator_with_its_row_and_an_empty_reset_records_nothing()
    {
        var harness = Harness.Create();
        await harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli);
        harness.Recorder.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();

        harness = await Harness.EnrolledAsync();
        await harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli);

        (await harness.Mfa.IsEnrolledAsync(harness.UserId)).Should().BeFalse();
        var reset = harness.Recorder.Operations.Last();
        reset.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        reset.Staged.Should().ContainSingle(e => e.Action == AuditActionNames.MfaReset && e.Target!.Id == harness.UserId.Value);
    }

    [Fact]
    public async Task Valid_totp_succeeds_and_invalid_fails()
    {
        var harness = await Harness.EnrolledAsync();
        var secret = harness.PlaintextSecret!;
        var code = Rfc6238Totp.Compute(secret, harness.Clock.GetUtcNow().ToUnixTimeSeconds());

        (await harness.Mfa.VerifyTotpAsync(harness.UserId, code)).Should().BeTrue();
        (await harness.Mfa.VerifyTotpAsync(harness.UserId, "111111")).Should().BeFalse();
        (await harness.Mfa.VerifyTotpAsync(harness.UserId, code)).Should().BeFalse();
    }

    [Fact]
    public async Task A_sign_in_presents_the_identity_of_the_credential_it_checked_and_the_step_its_code_matched()
    {
        var harness = await Harness.EnrolledAsync();
        var checkedCredential = harness.Totp.Stored!.CredentialId;
        var unix = harness.Clock.GetUtcNow().ToUnixTimeSeconds();

        (await harness.Mfa.VerifyTotpAsync(harness.UserId, Rfc6238Totp.Compute(harness.PlaintextSecret!, unix))).Should().BeTrue();

        harness.Totp.StepWrites.Should().Equal((checkedCredential, unix / Rfc6238Totp.DefaultPeriodSeconds));
    }

    [Fact]
    public async Task A_valid_code_whose_step_write_changes_nothing_does_not_sign_in()
    {
        // The code is valid for the credential the service read; the conditional write then changes no row. The code proves nothing.
        var harness = await Harness.EnrolledAsync();
        var before = harness.Totp.Stored;
        harness.Totp.RefuseNextStepWrite = true;
        var code = Rfc6238Totp.Compute(harness.PlaintextSecret!, harness.Clock.GetUtcNow().ToUnixTimeSeconds());

        (await harness.Mfa.VerifyTotpAsync(harness.UserId, code)).Should().BeFalse();

        harness.Totp.StepWrites.Should().ContainSingle("the service asked for the step to be recorded and was refused");
        harness.Totp.Stored.Should().Be(before);
    }

    [Fact]
    public async Task A_code_of_an_authenticator_replaced_after_it_was_checked_does_not_sign_in_and_records_nothing()
    {
        // A switch commits between the service's read and its write: new identity, and the step of the candidate's verification code
        // (lower than the stale code's step, so only the identity condition can refuse it).
        var harness = await Harness.EnrolledAsync();
        var unix = harness.Clock.GetUtcNow().ToUnixTimeSeconds();
        var switchedStep = unix / Rfc6238Totp.DefaultPeriodSeconds - 1;
        harness.Totp.BeforeNextStepWrite = () => harness.Totp.Replace(c => c with { CredentialId = "switched", LastUsedStep = switchedStep });

        (await harness.Mfa.VerifyTotpAsync(harness.UserId, Rfc6238Totp.Compute(harness.PlaintextSecret!, unix))).Should().BeFalse();

        harness.Totp.Stored!.LastUsedStep.Should().Be(switchedStep, "a sign-in with the retired secret records nothing on the new one");
    }

    [Fact]
    public async Task Of_two_sign_ins_that_checked_the_same_code_only_the_first_to_record_its_step_signs_in()
    {
        var harness = await Harness.EnrolledAsync();
        var unix = harness.Clock.GetUtcNow().ToUnixTimeSeconds();
        var code = Rfc6238Totp.Compute(harness.PlaintextSecret!, unix);
        // The other sign-in read the same row, checked the same code and recorded its step first.
        harness.Totp.BeforeNextStepWrite = () => harness.Totp.Replace(c => c with { LastUsedStep = unix / Rfc6238Totp.DefaultPeriodSeconds });

        (await harness.Mfa.VerifyTotpAsync(harness.UserId, code)).Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_code_succeeds_once_then_fails()
    {
        var harness = await Harness.EnrolledAsync();
        var code = harness.RecoveryCodes[0];

        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, code)).Should().BeTrue();
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, code)).Should().BeFalse();
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, "XXXX-XXXX-XXXX")).Should().BeFalse();

        // One row, for the one code that was consumed; the user signing in is the actor and the target.
        var consumed = harness.Recorder.Operations.Should().ContainSingle(o => o.Completed).Which;
        consumed.Operation.Actor.Should().Be(AuditActor.AuthenticatedUser(harness.UserId));
        var row = consumed.Staged.Should().ContainSingle().Which;
        row.Action.Should().Be(AuditActionNames.RecoveryCodeConsumed);
        row.Target!.Id.Should().Be(harness.UserId.Value);
        row.Fields["remaining"].Should().Be(9);
        harness.Recorder.Operations.Count(o => o.NoOp).Should().Be(2, "a reused or wrong code changes nothing and records nothing");
        harness.RecordedText().Should().NotContain(code);
    }

    [Fact]
    public async Task Regeneration_invalidates_previous_codes()
    {
        var harness = await Harness.EnrolledAsync();
        var previous = harness.RecoveryCodes[0];

        var next = await harness.Mfa.RegenerateRecoveryCodesAsync(harness.UserId, AuditActor.EmergencyCli);
        next.Should().NotContain(previous);
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, previous)).Should().BeFalse();
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, next[0])).Should().BeTrue();

        var regenerated = harness.Recorder.Single(AuditActionNames.RecoveryCodesGenerated);
        regenerated.Fields["regenerated"].Should().Be(true);
        harness.Recorder.Operations.First().Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        harness.RecordedText().Should().NotContain(next);
    }

    [Fact]
    public async Task Emergency_user_requires_mfa_even_before_enrollment()
    {
        var harness = Harness.Create();
        var user = new User(new UserId("usr-e"), "Emergency", [], [], IsBreakGlassEligible: true) { Username = "emergency" };
        (await harness.Mfa.IsRequiredAsync(user)).Should().BeTrue();
    }

    private sealed class Harness
    {
        public UserId UserId { get; } = new("usr-e");
        public NativeMfaService Mfa { get; private set; } = null!;
        public MemoryTotp Totp { get; } = new();
        public MemoryRecovery Recovery { get; } = new();
        public MemoryCandidates Candidates { get; }
        public RecordingAuditedUnitOfWork Recorder { get; private set; } = new();
        public RecordingAuditEventWriter Events { get; } = new();
        public RecordingGate Gate { get; } = new();
        public ScriptedViabilityReads Reads { get; private set; } = null!;
        public byte[]? PlaintextSecret { get; private set; }
        public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];
        public FixedTime Clock { get; } = new(Now);

        private Harness()
        {
            Candidates = new MemoryCandidates(this);
            // By default an ordinary administrator who can sign in without Microsoft remains whatever the operation does, so a decision
            // that reads no facts allows it; a case about the decision scripts its own states.
            UseViability(ViabilityTestData.Same(["fallback-admin"], ["fallback-admin"]));
        }

        /// <summary>The accounts the cases name; the service loads the account before it changes anything of it.</summary>
        private static InMemoryUserRepository Accounts() =>
            new(new[] { "usr-e", "usr-stale", "usr-race", "usr-retry" }
                .Select(id => new User(new UserId(id), id, [], [], IsBreakGlassEligible: false) { Username = id })
                .ToArray());

        /// <summary>
        /// Makes the recording unit put the authenticator, the codes and the candidate back when an attempt rolls back (a refusal declares a
        /// no-op after the change was applied, or the attempt throws), as the SQL unit does.
        /// </summary>
        public Harness RollingBack()
        {
            Recorder.CaptureState = () =>
            {
                var stored = Totp.Stored;
                var codes = Recovery.Codes.ToList();
                var candidate = Candidates.Candidate;
                return () =>
                {
                    Totp.Replace(_ => stored!);
                    Recovery.Restore(codes);
                    Candidates.Candidate = candidate;
                };
            };
            return this;
        }

        /// <summary>Rebuilds the service over the real coordinator and guard with the given states, read in order (the last repeats).</summary>
        public Harness UseViability(params AdministratorViabilityState[] states)
        {
            Reads = new ScriptedViabilityReads(states);
            return Rebuild();
        }

        /// <summary>Rebuilds the service over a coordinator that builds <paramref name="deployment"/> when an operation prepares (R-1).</summary>
        public Harness UseDeployment(IDeploymentAuthenticationConfiguration deployment)
        {
            Deployment = deployment;
            return Rebuild();
        }

        private IDeploymentAuthenticationConfiguration? Deployment { get; set; }

        private Harness Rebuild()
        {
            Mfa = new NativeMfaService(
                Totp,
                Recovery,
                new LocalPasswordHasher(),
                new XorProtector(),
                Clock,
                NullLogger<NativeMfaService>.Instance,
                new ForwardingUnit(this),
                new AdministratorViabilityCoordinator(Gate, Reads, Deployment),
                Events,
                Accounts(),
                Candidates);
            return this;
        }

        public static Harness Create() => new();

        public static async Task<Harness> EnrolledAsync()
        {
            var harness = new Harness();
            var challenge = await harness.Mfa.BeginEnrollmentAsync(harness.UserId, "emergency");
            harness.PlaintextSecret = Base32.Decode(challenge.ManualEntryKey);
            var code = Rfc6238Totp.Compute(harness.PlaintextSecret, Now.ToUnixTimeSeconds());
            harness.RecoveryCodes = await harness.Mfa.CompleteEnrollmentAsync(harness.UserId, code, AuditActor.EmergencyCli);
            harness.Clock.Advance(TimeSpan.FromSeconds(Rfc6238Totp.DefaultPeriodSeconds));
            harness.Recorder = new RecordingAuditedUnitOfWork();
            return harness;
        }

        /// <summary>Every string any recorded row holds, to prove no code or secret reached the audit trail.</summary>
        public IReadOnlyList<string> RecordedText() =>
            Recorder.Operations.SelectMany(o => o.Staged).SelectMany(e => e.Fields.Values).Select(v => v?.ToString() ?? "").ToList();

        /// <summary>Lets a test start a fresh recording after setup without rebuilding the service.</summary>
        private sealed class ForwardingUnit(Harness harness) : IAuditedUnitOfWork
        {
            public Task<T> ExecuteAsync<T>(
                AuditedOperation operation,
                Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
                Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
                CancellationToken cancellationToken = default) =>
                harness.Recorder.ExecuteAsync(operation, attempt, reconstruct, cancellationToken);
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default)
        {
            var copy = plaintext.ToArray();
            for (var i = 0; i < copy.Length; i++)
                copy[i] ^= 0xA5;
            return ValueTask.FromResult(copy);
        }

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
            => ProtectAsync(protectedPayload, keyId, cancellationToken);
    }

    private sealed class MemoryTotp : INativeTotpCredentialRepository
    {
        private NativeTotpStoredCredential? _stored;
        public byte[]? LastProtectedSecret { get; private set; }

        public ValueTask<NativeTotpStoredCredential?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_stored);

        /// <summary>Plays an enrolment that completes between the service's check and its pending write.</summary>
        public bool RefuseNextPendingWrite { get; set; }

        public ValueTask<bool> UpsertPendingAsync(UserId userId, byte[] protectedSecret, string protectorKeyId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
        {
            // As the repository: an enrolled credential is never replaced by a pending one.
            if (RefuseNextPendingWrite)
            {
                RefuseNextPendingWrite = false;
                return ValueTask.FromResult(false);
            }

            if (_stored is { IsEnrolled: true })
                return ValueTask.FromResult(false);
            LastProtectedSecret = protectedSecret;
            _stored = new NativeTotpStoredCredential(userId, protectedSecret, protectorKeyId, false, null, startedAtUtc, null, null, Guid.NewGuid().ToString("N"));
            return ValueTask.FromResult(true);
        }

        /// <summary>Plays a begin that replaces the pending secret after the enrollment checked its code and before it enrolls.</summary>
        public Action? BeforeNextEnrol { get; set; }

        public ValueTask<bool> MarkEnrolledAsync(UserId userId, string expectedCredentialId, long lastUsedStep, DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default)
        {
            var interleaved = BeforeNextEnrol;
            BeforeNextEnrol = null;
            interleaved?.Invoke();

            // As the repository: only the still-pending credential that was verified is enrolled.
            if (_stored is not { IsEnrolled: false } pending || pending.CredentialId != expectedCredentialId)
                return ValueTask.FromResult(false);
            _stored = pending with { IsEnrolled = true, LastUsedStep = lastUsedStep, EnrolledAtUtc = enrolledAtUtc, LastVerifiedAtUtc = enrolledAtUtc };
            return ValueTask.FromResult(true);
        }

        /// <summary>Plays a write that changes no row although the code was valid when checked (the store refused it).</summary>
        public bool RefuseNextStepWrite { get; set; }

        /// <summary>Plays another writer (a switch, a reset and new enrolment, a concurrent sign-in) between the service's check and its step write.</summary>
        public Action? BeforeNextStepWrite { get; set; }

        /// <summary>The identity and step each step write presented.</summary>
        public List<(string ExpectedCredentialId, long Step)> StepWrites { get; } = [];

        /// <summary>Replaces the stored credential, as a committed switch or a reset followed by a new enrolment would.</summary>
        public void Replace(Func<NativeTotpStoredCredential, NativeTotpStoredCredential> change) => _stored = change(_stored!);

        public NativeTotpStoredCredential? Stored => _stored;

        public ValueTask<bool> UpdateLastUsedAsync(UserId userId, string expectedCredentialId, long matchedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default)
        {
            var interleaved = BeforeNextStepWrite;
            BeforeNextStepWrite = null;
            interleaved?.Invoke();
            StepWrites.Add((expectedCredentialId, matchedStep));
            if (RefuseNextStepWrite)
            {
                RefuseNextStepWrite = false;
                return ValueTask.FromResult(false);
            }

            // As the repository: the enrolled credential that was checked, and a step newer than any already consumed.
            if (_stored is not { IsEnrolled: true } enrolled
                || enrolled.CredentialId != expectedCredentialId
                || (enrolled.LastUsedStep is { } last && last >= matchedStep))
            {
                return ValueTask.FromResult(false);
            }

            _stored = enrolled with { LastUsedStep = matchedStep, LastVerifiedAtUtc = verifiedAtUtc };
            return ValueTask.FromResult(true);
        }

        /// <summary>Plays a tracked delete that finds the row changed since it was read (a sign-in recorded a step in between).</summary>
        public bool ConcurrencyFailureOnNextDelete { get; set; }

        public ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            if (ConcurrencyFailureOnNextDelete)
            {
                ConcurrencyFailureOnNextDelete = false;
                throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Injected: the credential row changed since it was read.");
            }

            _stored = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryRecovery : INativeMfaRecoveryCodeRepository
    {
        private readonly List<NativeMfaRecoveryCodeRecord> _codes = [];
        public bool PlaintextNeverStored => _codes.TrueForAll(c => !c.CodeHash.Contains('-') && c.CodeHash.Length > 16);

        public ValueTask<IReadOnlyList<NativeMfaRecoveryCodeRecord>> ListAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<NativeMfaRecoveryCodeRecord>>(_codes.ToList());

        public ValueTask ReplaceAllAsync(UserId userId, IReadOnlyList<NativeMfaRecoveryCodeRecord> codes, CancellationToken cancellationToken = default)
        {
            _codes.Clear();
            _codes.AddRange(codes);
            return ValueTask.CompletedTask;
        }

        /// <summary>Plays another sign-in that consumed the same code after this one read it as unused.</summary>
        public bool ConsumedByAnotherSignIn { get; set; }

        public ValueTask<bool> MarkConsumedAsync(UserId userId, string codeId, DateTimeOffset consumedAtUtc, CancellationToken cancellationToken = default)
        {
            // As the repository: the code must be this user's and still unused, judged as it writes.
            var idx = _codes.FindIndex(c => c.Id == codeId && c.UserId == userId);
            if (ConsumedByAnotherSignIn && idx >= 0)
            {
                ConsumedByAnotherSignIn = false;
                _codes[idx] = _codes[idx] with { ConsumedAtUtc = consumedAtUtc.AddSeconds(-1) };
            }

            if (idx < 0 || _codes[idx].ConsumedAtUtc is not null)
                return ValueTask.FromResult(false);
            _codes[idx] = _codes[idx] with { ConsumedAtUtc = consumedAtUtc };
            return ValueTask.FromResult(true);
        }

        public IReadOnlyList<NativeMfaRecoveryCodeRecord> Codes => _codes;

        public void Restore(IEnumerable<NativeMfaRecoveryCodeRecord> codes)
        {
            _codes.Clear();
            _codes.AddRange(codes);
        }

        public ValueTask DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            _codes.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
