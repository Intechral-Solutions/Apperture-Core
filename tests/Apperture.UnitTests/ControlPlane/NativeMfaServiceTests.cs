using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
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

        var act = () => harness.Mfa.CompleteEnrollmentAsync(userId, "000000").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();

        var secret = Base32.Decode(challenge.ManualEntryKey);
        var code = Rfc6238Totp.Compute(secret, Now.ToUnixTimeSeconds());
        var recoveryCodes = await harness.Mfa.CompleteEnrollmentAsync(userId, code);

        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeTrue();
        recoveryCodes.Should().HaveCount(10);
        harness.Totp.LastProtectedSecret.Should().NotBeNull();
        Convert.ToBase64String(harness.Totp.LastProtectedSecret!).Should().NotBe(Convert.ToBase64String(secret));
        harness.Recovery.PlaintextNeverStored.Should().BeTrue();
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.MfaEnrolled);
        harness.Audit.Details.Should().NotContain(code);
        harness.Audit.Details.Should().NotContain(recoveryCodes[0]);
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
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.RecoveryCodeConsumed);
        harness.Audit.Details.Should().NotContain(code);
    }

    [Fact]
    public async Task Regeneration_invalidates_previous_codes()
    {
        var harness = await Harness.EnrolledAsync();
        var previous = harness.RecoveryCodes[0];

        var next = await harness.Mfa.RegenerateRecoveryCodesAsync(harness.UserId);
        next.Should().NotContain(previous);
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, previous)).Should().BeFalse();
        (await harness.Mfa.ConsumeRecoveryCodeAsync(harness.UserId, next[0])).Should().BeTrue();
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
        public RecordingAudit Audit { get; } = new();
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
                Audit,
                Clock,
                NullLogger<NativeMfaService>.Instance);
        }

        public static Harness Create() => new();

        public static async Task<Harness> EnrolledAsync()
        {
            var harness = new Harness();
            var challenge = await harness.Mfa.BeginEnrollmentAsync(harness.UserId, "emergency");
            harness.PlaintextSecret = Base32.Decode(challenge.ManualEntryKey);
            var code = Rfc6238Totp.Compute(harness.PlaintextSecret, Now.ToUnixTimeSeconds());
            harness.RecoveryCodes = await harness.Mfa.CompleteEnrollmentAsync(harness.UserId, code);
            harness.Clock.Advance(TimeSpan.FromSeconds(Rfc6238Totp.DefaultPeriodSeconds));
            harness.Audit.Actions.Clear();
            harness.Audit.Details.Clear();
            return harness;
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

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public List<string> Details { get; } = [];
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            if (record.Details is not null)
                Details.Add(record.Details);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class FixedTime : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
