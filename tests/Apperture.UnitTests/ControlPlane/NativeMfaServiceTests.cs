using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NativeMfaServiceTests
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
        public NativeMfaService Mfa { get; }
        public MemoryTotp Totp { get; } = new();
        public MemoryRecovery Recovery { get; } = new();
        public RecordingAuditedUnitOfWork Recorder { get; private set; } = new();
        public byte[]? PlaintextSecret { get; private set; }
        public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];
        public FixedTime Clock { get; } = new(Now);

        private Harness()
        {
            Mfa = new NativeMfaService(
                Totp,
                Recovery,
                new LocalPasswordHasher(),
                new XorProtector(),
                Clock,
                NullLogger<NativeMfaService>.Instance,
                new ForwardingUnit(this));
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

        public ValueTask UpsertPendingAsync(UserId userId, byte[] protectedSecret, string protectorKeyId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
        {
            LastProtectedSecret = protectedSecret;
            _stored = new NativeTotpStoredCredential(userId, protectedSecret, protectorKeyId, false, null, startedAtUtc, null, null);
            return ValueTask.CompletedTask;
        }

        public ValueTask MarkEnrolledAsync(UserId userId, long lastUsedStep, DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default)
        {
            _stored = _stored! with { IsEnrolled = true, LastUsedStep = lastUsedStep, EnrolledAtUtc = enrolledAtUtc, LastVerifiedAtUtc = enrolledAtUtc };
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastUsedAsync(UserId userId, long lastUsedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default)
        {
            _stored = _stored! with { LastUsedStep = lastUsedStep, LastVerifiedAtUtc = verifiedAtUtc };
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default)
        {
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

        public ValueTask MarkConsumedAsync(string codeId, DateTimeOffset consumedAtUtc, CancellationToken cancellationToken = default)
        {
            var idx = _codes.FindIndex(c => c.Id == codeId);
            if (idx >= 0)
                _codes[idx] = _codes[idx] with { ConsumedAtUtc = consumedAtUtc };
            return ValueTask.CompletedTask;
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
