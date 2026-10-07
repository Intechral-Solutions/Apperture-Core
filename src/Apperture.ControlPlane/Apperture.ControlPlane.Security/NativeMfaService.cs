using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
/// <remarks>
/// AUD-1.2 (audit hardening design gate §13): enrollment, recovery-code generation, consumption and reset each commit with
/// their audit row. New recovery codes are generated once per operation, outside its transaction, and reused by a rerun,
/// so the codes returned are always the codes stored; they are never recorded.
/// </remarks>
public sealed class NativeMfaService : INativeMfaService
{
    public const string TotpProtectorKeyId = "apperture.native-mfa.totp";

    private readonly INativeTotpCredentialRepository _totp;
    private readonly INativeMfaRecoveryCodeRepository _recoveryCodes;
    private readonly ILocalPasswordHasher _hasher;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _time;
    private readonly ILogger<NativeMfaService> _logger;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public NativeMfaService(
        INativeTotpCredentialRepository totp,
        INativeMfaRecoveryCodeRepository recoveryCodes,
        ILocalPasswordHasher hasher,
        ISecretProtector protector,
        TimeProvider time,
        ILogger<NativeMfaService> logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _totp = totp;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _protector = protector;
        _time = time;
        _logger = logger;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Native MFA changes require the audited unit of work.");

    /// <inheritdoc />
    public async ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var stored = await _totp.GetByUserIdAsync(userId, cancellationToken);
        return stored is { IsEnrolled: true };
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.IsBreakGlassEligible)
            return true;
        return await IsEnrolledAsync(user.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(
        UserId userId,
        string accountName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _totp.GetByUserIdAsync(userId, cancellationToken);
        if (existing is { IsEnrolled: true })
        {
            throw new InvalidOperationException(
                "An authenticator is already enrolled. Reset native MFA before enrolling again.");
        }

        var secret = Rfc6238Totp.GenerateSecret();
        var protectedSecret = await _protector.ProtectAsync(secret, TotpProtectorKeyId, cancellationToken);
        var now = _time.GetUtcNow();
        await _totp.UpsertPendingAsync(userId, protectedSecret, TotpProtectorKeyId, now, cancellationToken);

        return new NativeMfaEnrollmentChallenge(
            userId,
            Rfc6238Totp.FormatOtpAuthUri(accountName, secret),
            Base32.Encode(secret),
            Array.Empty<string>());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(
        UserId userId,
        string totpCode,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var codes = NewRecoveryCodes(userId);

        // The enrollment, its recovery codes and both rows commit together.
        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.MfaEnrolled, actor),
            async (transaction, ct) =>
            {
                var stored = await _totp.GetByUserIdAsync(userId, ct)
                    ?? throw new InvalidOperationException("No pending authenticator enrollment exists.");
                if (stored.IsEnrolled)
                    throw new InvalidOperationException("An authenticator is already enrolled.");

                var secret = await UnprotectSecretAsync(stored, ct);
                var unix = _time.GetUtcNow().ToUnixTimeSeconds();
                if (!Rfc6238Totp.Verify(secret, totpCode, unix, stored.LastUsedStep, out var matchedStep))
                    throw new InvalidOperationException("The authenticator code is not valid.");

                await _totp.MarkEnrolledAsync(userId, matchedStep, _time.GetUtcNow(), ct);
                await _recoveryCodes.ReplaceAllAsync(userId, codes.Records, ct);
                await transaction.StageAsync(AuditEvents.MfaEnrolled(userId), ct);
                await transaction.StageAsync(AuditEvents.RecoveryCodesGenerated(userId, codes.Plaintext.Count, regenerated: false), ct);
                return codes.Plaintext;
            },
            (_, _) => Task.FromResult(codes.Plaintext),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default)
    {
        var stored = await _totp.GetByUserIdAsync(userId, cancellationToken);
        if (stored is not { IsEnrolled: true })
            return false;

        var secret = await UnprotectSecretAsync(stored, cancellationToken);
        var unix = _time.GetUtcNow().ToUnixTimeSeconds();
        if (!Rfc6238Totp.Verify(secret, totpCode, unix, stored.LastUsedStep, out var matchedStep))
            return false;

        await _totp.UpdateLastUsedAsync(userId, matchedStep, _time.GetUtcNow(), cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default)
    {
        var normalized = MfaRecoveryCodes.Normalize(recoveryCode);
        if (normalized.Length == 0)
            return false;

        // The sign-in has no session yet: the user whose password and pending ticket this request verified is the actor.
        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryCodeConsumed, AuditActor.AuthenticatedUser(userId)),
            async (transaction, ct) =>
            {
                var stored = await _totp.GetByUserIdAsync(userId, ct);
                if (stored is not { IsEnrolled: true })
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                var records = await _recoveryCodes.ListAsync(userId, ct);
                var unused = records.Where(r => r.ConsumedAtUtc is null).ToList();
                var match = unused.FirstOrDefault(r => _hasher.VerifyPassword(normalized, r.CodeHash, r.Salt));
                if (match is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _recoveryCodes.MarkConsumedAsync(match.Id, _time.GetUtcNow(), ct);
                await transaction.StageAsync(AuditEvents.RecoveryCodeConsumed(userId, unused.Count - 1), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(
        UserId userId,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var codes = NewRecoveryCodes(userId);
        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryCodesGenerated, actor),
            async (transaction, ct) =>
            {
                if (!await IsEnrolledAsync(userId, ct))
                    throw new InvalidOperationException("Native MFA must be enrolled before recovery codes can be generated.");

                await _recoveryCodes.ReplaceAllAsync(userId, codes.Records, ct);
                await transaction.StageAsync(AuditEvents.RecoveryCodesGenerated(userId, codes.Plaintext.Count, regenerated: true), ct);
                return codes.Plaintext;
            },
            (_, _) => Task.FromResult(codes.Plaintext),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.MfaReset, actor),
            async (transaction, ct) =>
            {
                // Nothing enrolled or pending and no codes: nothing changes and nothing is recorded.
                var stored = await _totp.GetByUserIdAsync(userId, ct);
                var records = await _recoveryCodes.ListAsync(userId, ct);
                if (stored is null && records.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await _totp.DeleteAsync(userId, ct);
                await _recoveryCodes.DeleteAllAsync(userId, ct);
                await transaction.StageAsync(AuditEvents.MfaReset(userId), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var records = await _recoveryCodes.ListAsync(userId, cancellationToken);
        return records.Count(r => r.ConsumedAtUtc is null);
    }

    private async ValueTask<byte[]> UnprotectSecretAsync(NativeTotpStoredCredential stored, CancellationToken cancellationToken)
    {
        return await _protector.UnprotectAsync(stored.ProtectedSecret, stored.ProtectorKeyId, cancellationToken);
    }

    /// <summary>A new set of recovery codes: the plaintext to show once, and the hashed records to store.</summary>
    private (IReadOnlyList<string> Plaintext, IReadOnlyList<NativeMfaRecoveryCodeRecord> Records) NewRecoveryCodes(UserId userId)
    {
        var plaintext = MfaRecoveryCodes.Generate();
        var records = new List<NativeMfaRecoveryCodeRecord>(plaintext.Count);
        foreach (var code in plaintext)
        {
            var normalized = MfaRecoveryCodes.Normalize(code);
            var (hash, salt) = _hasher.HashPassword(normalized);
            records.Add(new NativeMfaRecoveryCodeRecord(
                Guid.NewGuid().ToString("N"),
                userId,
                hash,
                salt,
                _hasher.AlgorithmName,
                ConsumedAtUtc: null));
        }

        return (plaintext, records);
    }
}
