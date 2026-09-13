using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class NativeMfaService : INativeMfaService
{
    public const string TotpProtectorKeyId = "apperture.native-mfa.totp";
    private const string AuditCategory = "auth";

    private readonly INativeTotpCredentialRepository _totp;
    private readonly INativeMfaRecoveryCodeRepository _recoveryCodes;
    private readonly ILocalPasswordHasher _hasher;
    private readonly ISecretProtector _protector;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<NativeMfaService> _logger;

    public NativeMfaService(
        INativeTotpCredentialRepository totp,
        INativeMfaRecoveryCodeRepository recoveryCodes,
        ILocalPasswordHasher hasher,
        ISecretProtector protector,
        IAuditService audit,
        TimeProvider time,
        ILogger<NativeMfaService> logger)
    {
        _totp = totp;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _protector = protector;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

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
        CancellationToken cancellationToken = default)
    {
        var stored = await _totp.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("No pending authenticator enrollment exists.");
        if (stored.IsEnrolled)
            throw new InvalidOperationException("An authenticator is already enrolled.");

        var secret = await UnprotectSecretAsync(stored, cancellationToken);
        var unix = _time.GetUtcNow().ToUnixTimeSeconds();
        if (!Rfc6238Totp.Verify(secret, totpCode, unix, stored.LastUsedStep, out var matchedStep))
            throw new InvalidOperationException("The authenticator code is not valid.");

        var now = _time.GetUtcNow();
        await _totp.MarkEnrolledAsync(userId, matchedStep, now, cancellationToken);
        var codes = await PersistNewRecoveryCodesAsync(userId, cancellationToken);
        await AuditAsync(EmergencyAccessAuditActionNames.MfaEnrolled, userId.Value, $"userId={userId.Value}", cancellationToken);
        await AuditAsync(EmergencyAccessAuditActionNames.RecoveryCodesGenerated, userId.Value, $"userId={userId.Value} count={codes.Count}", cancellationToken);
        return codes;
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

        var stored = await _totp.GetByUserIdAsync(userId, cancellationToken);
        if (stored is not { IsEnrolled: true })
            return false;

        var records = await _recoveryCodes.ListAsync(userId, cancellationToken);
        foreach (var record in records)
        {
            if (record.ConsumedAtUtc is not null)
                continue;
            if (!_hasher.VerifyPassword(normalized, record.CodeHash, record.Salt))
                continue;

            await _recoveryCodes.MarkConsumedAsync(record.Id, _time.GetUtcNow(), cancellationToken);
            await AuditAsync(
                EmergencyAccessAuditActionNames.RecoveryCodeConsumed,
                userId.Value,
                $"userId={userId.Value}",
                cancellationToken);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsEnrolledAsync(userId, cancellationToken))
            throw new InvalidOperationException("Native MFA must be enrolled before recovery codes can be generated.");

        var codes = await PersistNewRecoveryCodesAsync(userId, cancellationToken);
        await AuditAsync(
            EmergencyAccessAuditActionNames.RecoveryCodesGenerated,
            userId.Value,
            $"userId={userId.Value} count={codes.Count} regenerated=true",
            cancellationToken);
        return codes;
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default)
    {
        await _totp.DeleteAsync(userId, cancellationToken);
        await _recoveryCodes.DeleteAllAsync(userId, cancellationToken);
        await AuditAsync(
            EmergencyAccessAuditActionNames.MfaReset,
            actorId?.Value ?? userId.Value,
            $"userId={userId.Value}",
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

    private async ValueTask<IReadOnlyList<string>> PersistNewRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken)
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

        await _recoveryCodes.ReplaceAllAsync(userId, records, cancellationToken);
        return plaintext;
    }

    private async ValueTask AuditAsync(string action, string actorId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(actorId),
            action,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            AuditCategory,
            _time.GetUtcNow(),
            new CorrelationId(null),
            details);

        try
        {
            await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append {AuditAction} audit record.", action);
        }
    }
}
