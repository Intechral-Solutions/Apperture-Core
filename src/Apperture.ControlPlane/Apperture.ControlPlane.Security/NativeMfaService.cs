using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
/// <remarks>
/// <para>AUD-1.2 (audit hardening design gate §13): enrollment, recovery-code generation, consumption, reset and replacement each commit
/// with their audit row. New recovery codes are generated once per operation, outside its transaction, and reused by a rerun, so the codes
/// returned are always the codes stored; they are never recorded.</para>
/// <para>IAM-10.2b: an authenticator and its recovery codes are inputs of the Recovery Access posture. A first enrolment and a regeneration
/// of the codes can only add, so each takes the global administrator-set gate and asserts that it reduced nothing. A bare reset can take a
/// healthy posture away, so it is a reducer: the gate, the state before, the removal, and the decision last, on the state it left. The
/// replacement is the normal way to change an authenticator: a candidate is staged and its possession proven with nothing held across the
/// operator's input, then a short switch under the gate makes it the active authenticator and is judged on the posture alone. Every
/// administrative operation names the account by the id of the user row it loaded, never by the caller's spelling of it. Signing in
/// (a TOTP code, a recovery code) takes no gate.</para>
/// </remarks>
public sealed class NativeMfaService : INativeMfaService
{
    public const string TotpProtectorKeyId = "apperture.native-mfa.totp";

    /// <summary>
    /// How long a staged replacement lives, by the database's clock: the operator's time to add it to an authenticator app and type a code,
    /// plus the wait for the gate at the switch.
    /// </summary>
    public static readonly TimeSpan ReplacementCandidateLifetime = TimeSpan.FromMinutes(10);

    private readonly INativeTotpCredentialRepository _totp;
    private readonly INativeMfaRecoveryCodeRepository _recoveryCodes;
    private readonly ILocalPasswordHasher _hasher;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _time;
    private readonly ILogger<NativeMfaService> _logger;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAdministratorViabilityCoordinator? _viability;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly IUserRepository? _users;
    private readonly INativeMfaReplacementCandidateRepository? _candidates;

    public NativeMfaService(
        INativeTotpCredentialRepository totp,
        INativeMfaRecoveryCodeRepository recoveryCodes,
        ILocalPasswordHasher hasher,
        ISecretProtector protector,
        TimeProvider time,
        ILogger<NativeMfaService> logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAdministratorViabilityCoordinator? viability = null,
        IAuditEventWriter? auditEvents = null,
        IUserRepository? users = null,
        INativeMfaReplacementCandidateRepository? candidates = null)
    {
        _totp = totp;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _protector = protector;
        _time = time;
        _logger = logger;
        _auditedUnitOfWork = auditedUnitOfWork;
        _viability = viability;
        _auditEvents = auditEvents;
        _users = users;
        _candidates = candidates;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Native MFA changes require the audited unit of work.");

    /// <summary>The global administrator-set gate and the viability reads. Absent is a wiring defect: an authenticator is never changed without them.</summary>
    private IAdministratorViabilityCoordinator Viability =>
        _viability ?? throw new InvalidOperationException("Native MFA administration requires the administrator viability coordinator.");

    /// <summary>Writes a refusal row after the rollback. Absent is a wiring defect.</summary>
    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Native MFA administration requires the audit event writer.");

    /// <summary>The user rows an administrative operation names the account by. Absent is a wiring defect.</summary>
    private IUserRepository Users =>
        _users ?? throw new InvalidOperationException("Native MFA administration requires the user repository.");

    /// <summary>The staged replacements. Absent is a wiring defect.</summary>
    private INativeMfaReplacementCandidateRepository Candidates =>
        _candidates ?? throw new InvalidOperationException("Replacing an authenticator requires the replacement candidate repository.");

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
        // A pending secret is not a posture input (only an enrolled authenticator is), and the write below never replaces an enrolled
        // one, so beginning takes no gate. The row is keyed by the id of the user row loaded here.
        var owner = await AccountOfAsync(userId, cancellationToken);
        var existing = await _totp.GetByUserIdAsync(owner, cancellationToken);
        if (existing is { IsEnrolled: true })
            throw AlreadyEnrolled();

        var secret = Rfc6238Totp.GenerateSecret();
        var protectedSecret = await _protector.ProtectAsync(secret, TotpProtectorKeyId, cancellationToken);
        var now = _time.GetUtcNow();

        // The check above is advisory: the write itself refuses to replace an enrolled credential, so an enrolment that completed
        // since is never undone. The secret generated here was not stored and is not returned.
        if (!await _totp.UpsertPendingAsync(owner, protectedSecret, TotpProtectorKeyId, now, cancellationToken))
            throw AlreadyEnrolled();

        return new NativeMfaEnrollmentChallenge(
            owner,
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
        var codes = NewRecoveryCodes();

        // The enrollment, its recovery codes and both rows commit together.
        return await TranslatingConcurrencyAsync(() => Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.MfaEnrolled, actor),
            async (transaction, ct) =>
            {
                // The administrator-set gate first: an enrolled authenticator is a posture input. A first enrolment can only add.
                var viability = await Viability.EnterAsync(ct);
                var owner = await AccountOfAsync(userId, ct);

                var stored = await _totp.GetByUserIdAsync(owner, ct)
                    ?? throw new InvalidOperationException("No pending authenticator enrollment exists.");
                if (stored.IsEnrolled)
                    throw new InvalidOperationException("An authenticator is already enrolled.");

                var secret = await UnprotectSecretAsync(stored, ct);
                var unix = _time.GetUtcNow().ToUnixTimeSeconds();
                if (!Rfc6238Totp.Verify(secret, totpCode, unix, stored.LastUsedStep, out var matchedStep))
                    throw new InvalidOperationException("The authenticator code is not valid.");

                await viability.CaptureBeforeForRaiseAsync(owner.Value, ct);

                // The write is conditional on the secret that was just checked still being the pending one: a begin that replaced it since
                // makes this fail, and the unit rolls back, so the account is never enrolled on a secret nobody verified.
                if (!await _totp.MarkEnrolledAsync(owner, stored.CredentialId, matchedStep, _time.GetUtcNow(), ct))
                {
                    throw new GovernanceConflictException(
                        GovernanceConflictCodes.AuthenticatorChanged,
                        "The pending authenticator enrollment changed while the code was being checked, so nothing was enrolled. Start the enrollment again.");
                }

                await _recoveryCodes.ReplaceAllAsync(owner, codes.For(owner), ct);
                await viability.AssertRaiseOnlyAsync(ct);
                await transaction.StageAsync(AuditEvents.MfaEnrolled(owner), ct);
                await transaction.StageAsync(AuditEvents.RecoveryCodesGenerated(owner, codes.Plaintext.Count, regenerated: false), ct);
                return codes.Plaintext;
            },
            (_, _) => Task.FromResult(codes.Plaintext),
            cancellationToken));
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

        // The code is accepted only if recording its step succeeds. The check above read the credential without a lock, so it is
        // advice: the write is conditional on that credential still being the one checked (not replaced by a switch or reset since)
        // and on the step being newer than any step already consumed (by a concurrent sign-in or the switch). When the write
        // changes nothing, the code proves nothing and the sign-in fails.
        return await _totp.UpdateLastUsedAsync(userId, stored.CredentialId, matchedStep, _time.GetUtcNow(), cancellationToken);
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

                // The list above was read without a lock, so the match is advice: the code is accepted only if this sign-in is the one
                // that consumes it. Another sign-in with the same code that consumed it first makes the write change nothing, and then the
                // code proves nothing.
                if (!await _recoveryCodes.MarkConsumedAsync(match.UserId, match.Id, _time.GetUtcNow(), ct))
                {
                    transaction.DeclareNoOp();
                    return false;
                }

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
        var codes = NewRecoveryCodes();
        return await TranslatingConcurrencyAsync(() => Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RecoveryCodesGenerated, actor),
            async (transaction, ct) =>
            {
                // The administrator-set gate first: the codes are a posture input. A new set of unused codes can only add.
                var viability = await Viability.EnterAsync(ct);
                var owner = await AccountOfAsync(userId, ct);
                if (await _totp.GetByUserIdAsync(owner, ct) is not { IsEnrolled: true })
                    throw new InvalidOperationException("Native MFA must be enrolled before recovery codes can be generated.");

                await viability.CaptureBeforeForRaiseAsync(owner.Value, ct);
                await _recoveryCodes.ReplaceAllAsync(owner, codes.For(owner), ct);
                await viability.AssertRaiseOnlyAsync(ct);
                await transaction.StageAsync(AuditEvents.RecoveryCodesGenerated(owner, codes.Plaintext.Count, regenerated: true), ct);
                return codes.Plaintext;
            },
            (_, _) => Task.FromResult(codes.Plaintext),
            cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        await TranslatingConcurrencyAsync(() => Unit.ExecuteRefusableAsync(
            Events,
            new AuditedOperation(AuditActionNames.MfaReset, actor),
            async (transaction, refusals, ct) =>
            {
                // The administrator-set gate first: a reset can take a healthy Recovery Access posture away.
                var viability = await Viability.EnterAsync(ct);
                var owner = await AccountOfAsync(userId, ct);

                // Nothing enrolled or pending and no codes: nothing changes and nothing is recorded, before the invariants.
                var stored = await _totp.GetByUserIdAsync(owner, ct);
                var records = await _recoveryCodes.ListAsync(owner, ct);
                if (stored is null && records.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await viability.CaptureBeforeAsync(ct);
                await _totp.DeleteAsync(owner, ct);
                await _recoveryCodes.DeleteAllAsync(owner, ct);

                // Last, after every write: the decision is made on the state the removal left, for the account as stored.
                var decision = await viability.VerifyBareResetAsync(owner.Value, ct);
                if (!decision.IsAllowed)
                    return refusals.Refuse<bool>(transaction, ViabilityRefusalAudit.Blocked(owner, decision), decision.Refusal());

                await transaction.StageAsync(AuditEvents.MfaReset(owner, decision.Evidence), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var records = await _recoveryCodes.ListAsync(userId, cancellationToken);
        return records.Count(r => r.ConsumedAtUtc is null);
    }

    // ------------------------------------------------------------------------------------------------ the replacement

    /// <inheritdoc />
    public async ValueTask<NativeMfaReplacementChallenge> BeginReplacementAsync(
        UserId userId,
        string accountName,
        CancellationToken cancellationToken = default)
    {
        // Staging holds no gate and no transaction across the operator's input, and changes nothing of the active authenticator or its
        // recovery codes: a candidate is not an authenticator until the switch.
        var owner = await AccountOfAsync(userId, cancellationToken);
        if (await _totp.GetByUserIdAsync(owner, cancellationToken) is not { IsEnrolled: true })
            throw NotEnrolled();

        var secret = Rfc6238Totp.GenerateSecret();
        var protectedSecret = await _protector.ProtectAsync(secret, TotpProtectorKeyId, cancellationToken);
        var staged = await Candidates.StageAsync(owner, protectedSecret, TotpProtectorKeyId, ReplacementCandidateLifetime, cancellationToken);

        // A stage refuses only for want of an enrolled authenticator (the check above is advice; the stage decides).
        if (!staged.IsApplied || staged.Candidate is not { } candidate)
            throw NotEnrolled();

        return new NativeMfaReplacementChallenge(
            owner,
            candidate.CandidateId,
            candidate.Version,
            Rfc6238Totp.FormatOtpAuthUri(accountName, secret),
            Base32.Encode(secret),
            candidate.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaReplacementVerification> VerifyReplacementAsync(
        UserId userId,
        string candidateId,
        int version,
        string totpCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);

        // No gate and no change to the active authenticator: possession of the candidate is proven against the candidate's own secret.
        var owner = await AccountOfAsync(userId, cancellationToken);
        var candidate = await Candidates.GetAsync(owner, candidateId, cancellationToken);
        if (candidate is null || candidate.Status is NativeMfaCandidateStatus.Superseded or NativeMfaCandidateStatus.Consumed)
            throw NoLongerValid();
        if (candidate.IsExpired)
        {
            await Candidates.DiscardAsync(owner, cancellationToken);
            throw Expired();
        }

        if (candidate.Status != NativeMfaCandidateStatus.Staged || candidate.Version != version)
            throw NoLongerValid();

        var active = await _totp.GetByUserIdAsync(owner, cancellationToken);
        if (active is not { IsEnrolled: true })
            throw NotEnrolled();
        if (!string.Equals(candidate.StagedAgainstCredentialId, active.CredentialId, StringComparison.Ordinal))
            throw StagedAgainstAnotherAuthenticator();

        // The code is checked against the candidate's secret, never the active one. A step at or below the active authenticator's last used
        // step is not accepted, so the step the switch records (this one) never moves backwards and this code is spent once switched.
        var secret = await _protector.UnprotectAsync(candidate.ProtectedSecret, candidate.ProtectorKeyId, cancellationToken);
        if (!Rfc6238Totp.Verify(secret, totpCode, _time.GetUtcNow().ToUnixTimeSeconds(), active.LastUsedStep, out var matchedStep))
            throw new InvalidOperationException("The authenticator code is not valid.");

        // The database decides, in one conditional statement (staged, this version, unexpired, this user, staged against the credential
        // that is active now): the reads above are advice.
        var verified = await Candidates.MarkVerifiedAsync(owner, candidate.CandidateId, version, matchedStep, cancellationToken);
        switch (verified.Outcome)
        {
            case NativeMfaCandidateOutcome.Applied:
                return new NativeMfaReplacementVerification(candidate.CandidateId, verified.Version!.Value);
            case NativeMfaCandidateOutcome.Expired:
                await Candidates.DiscardAsync(owner, cancellationToken);
                throw Expired();
            case NativeMfaCandidateOutcome.NoActiveEnrollment:
                throw NotEnrolled();
            case NativeMfaCandidateOutcome.ActiveEnrollmentChanged:
                throw StagedAgainstAnotherAuthenticator();
            default:
                throw NoLongerValid();
        }
    }

    /// <inheritdoc />
    public async ValueTask<NativeMfaReplacementResult> CompleteReplacementAsync(
        UserId userId,
        string candidateId,
        int version,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);

        // The new recovery codes, generated and hashed once, outside the unit: a rerun after a confirmed rollback stores the same codes, and
        // the codes returned are the codes stored. No operator input happens from here on.
        var codes = NewRecoveryCodes();
        var switchedAt = _time.GetUtcNow();
        try
        {
            return await Unit.ExecuteRefusableAsync(
                Events,
                new AuditedOperation(AuditActionNames.MfaReplaced, actor),
                async (transaction, refusals, ct) =>
                {
                    // The administrator-set gate first: the switch replaces the posture inputs (the authenticator and its codes).
                    var viability = await Viability.EnterAsync(ct);
                    var owner = await AccountOfAsync(userId, ct);

                    await viability.CaptureBeforeAsync(ct);
                    var switched = await Candidates.SwitchAsync(new NativeMfaSwitchRequest(owner, candidateId, version, codes.For(owner), switchedAt), ct);
                    if (!switched.IsApplied)
                    {
                        // A refused switch wrote nothing. A consumed candidate is not proof of anything by itself: it is this replacement,
                        // already applied by an earlier request, only if the active authenticator is still this candidate's.
                        if (switched.Outcome == NativeMfaCandidateOutcome.Consumed && await IsActiveAuthenticatorOfAsync(owner, candidateId, ct))
                        {
                            transaction.DeclareNoOp();
                            return NativeMfaReplacementResult.AppliedEarlier;
                        }

                        throw SwitchRefusal(switched.Outcome);
                    }

                    // Last, after every write: the switch is judged on its net effect on the posture alone (I-R3-1). A decision made on
                    // anything else means administrator access changed while it ran; whatever its verdict, nothing is kept.
                    var decision = await viability.VerifyReplacementAsync(owner.Value, ct);
                    if (decision.Basis != ViabilityDecisionBasis.PostureOnly)
                    {
                        return refusals.Refuse<NativeMfaReplacementResult>(
                            transaction,
                            ViabilityRefusalAudit.Blocked(owner, decision, GovernanceConflictCodes.AdministratorAccessChanged),
                            AdministratorAccessChanged());
                    }

                    if (!decision.IsAllowed)
                        return refusals.Refuse<NativeMfaReplacementResult>(transaction, ViabilityRefusalAudit.Blocked(owner, decision), decision.Refusal());

                    await transaction.StageAsync(AuditEvents.MfaReplaced(owner, decision.Evidence), ct);
                    await transaction.StageAsync(AuditEvents.RecoveryCodesGenerated(owner, codes.Plaintext.Count, regenerated: true), ct);
                    return new NativeMfaReplacementResult(AlreadyApplied: false, codes.Plaintext);
                },
                // A commit confirmed by verification is this operation's own: the codes stored are the codes generated above.
                (_, _) => Task.FromResult(new NativeMfaReplacementResult(AlreadyApplied: false, codes.Plaintext)),
                cancellationToken);
        }
        catch (GovernanceConflictException ex) when (ex.Code == GovernanceConflictCodes.AuthenticatorReplacementExpired)
        {
            // The expired candidate is removed once the unit has ended: inside it, the removal would have rolled back with the unit.
            await Candidates.DiscardAsync(await AccountOfAsync(userId, CancellationToken.None), CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> AbandonReplacementAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var owner = await AccountOfAsync(userId, cancellationToken);
        return await Candidates.DiscardAsync(owner, cancellationToken) > 0;
    }

    /// <summary>
    /// True when the active authenticator is the one this candidate's switch made: an enrolled credential whose identity is the candidate's
    /// id (the switch gives the new secret that identity), and the candidate consumed.
    /// </summary>
    private async ValueTask<bool> IsActiveAuthenticatorOfAsync(UserId owner, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await Candidates.GetAsync(owner, candidateId, cancellationToken);
        var active = await _totp.GetByUserIdAsync(owner, cancellationToken);
        return candidate is { Status: NativeMfaCandidateStatus.Consumed }
               && active is { IsEnrolled: true }
               && string.Equals(active.CredentialId, candidate.CandidateId, StringComparison.Ordinal);
    }

    /// <summary>What a switch that changed nothing tells the operator. Every outcome but a verified candidate's wrote nothing.</summary>
    private static Exception SwitchRefusal(NativeMfaCandidateOutcome outcome) => outcome switch
    {
        NativeMfaCandidateOutcome.NoActiveEnrollment => NotEnrolled(),
        NativeMfaCandidateOutcome.Expired => Expired(),
        NativeMfaCandidateOutcome.ActiveEnrollmentChanged => StagedAgainstAnotherAuthenticator(),
        NativeMfaCandidateOutcome.NotVerified => new AuditInvariantException(
            "The replacement was switched before possession of the new authenticator was proven; nothing was changed."),
        _ => NoLongerValid(),
    };

    // ------------------------------------------------------------------------------------------------ shared

    /// <summary>
    /// The account an administrative operation is about, named by the id of the user row loaded here: the database finds a user whatever the
    /// case of the id, and the viability decision and the facts compare ids ordinally, so nothing after this uses the caller's spelling.
    /// </summary>
    private async ValueTask<UserId> AccountOfAsync(UserId userId, CancellationToken cancellationToken) =>
        (await Users.GetByIdAsync(userId, cancellationToken) ?? throw new InvalidOperationException("User not found.")).Id;

    /// <summary>
    /// Runs a unit whose tracked writes (a credential removal, a code replacement) can meet a row another request changed after it was read.
    /// The unit has rolled back by then; the typed conflict says so and invites a repeat. It is caught once, here: a second automatic retry
    /// would hide the competing writer.
    /// </summary>
    private static async Task<T> TranslatingConcurrencyAsync<T>(Func<Task<T>> unit)
    {
        try
        {
            return await unit();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new GovernanceConflictException(
                GovernanceConflictCodes.AuthenticatorChanged,
                "The authenticator changed while this request ran, so nothing was changed. Run it again.",
                ex);
        }
    }

    private static InvalidOperationException AlreadyEnrolled() =>
        new("An authenticator is already enrolled. Replace it instead of enrolling again.");

    private static GovernanceConflictException NotEnrolled() =>
        new(GovernanceConflictCodes.AuthenticatorNotEnrolled,
            "There is no enrolled authenticator to replace, so nothing was changed. Enroll an authenticator instead.");

    private static GovernanceConflictException NoLongerValid() =>
        new(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid,
            "This replacement can no longer complete (a newer one was started, it was already used, or it changed while the code was being checked), so nothing was changed. Start the replacement again.");

    private static GovernanceConflictException Expired() =>
        new(GovernanceConflictCodes.AuthenticatorReplacementExpired,
            "This replacement expired before it was completed, so nothing was changed. Start the replacement again.");

    private static GovernanceConflictException StagedAgainstAnotherAuthenticator() =>
        new(GovernanceConflictCodes.AuthenticatorChanged,
            "The authenticator changed after this replacement was started, so nothing was changed. Start the replacement again.");

    private static GovernanceConflictException AdministratorAccessChanged() =>
        new(GovernanceConflictCodes.AdministratorAccessChanged,
            "Administrator access changed while the replacement ran, so the authenticator was not replaced and the active one stays in place. Run the replacement again.");

    private async ValueTask<byte[]> UnprotectSecretAsync(NativeTotpStoredCredential stored, CancellationToken cancellationToken)
    {
        return await _protector.UnprotectAsync(stored.ProtectedSecret, stored.ProtectorKeyId, cancellationToken);
    }

    /// <summary>
    /// A new set of recovery codes: the plaintext to show once, and the hashed codes to store. Generated before any unit; each operation
    /// gives them their owner (<see cref="NewCodes.For"/>) once it has loaded the account, so they belong to the user as stored.
    /// </summary>
    private NewCodes NewRecoveryCodes()
    {
        var plaintext = MfaRecoveryCodes.Generate();
        var hashed = new List<(string Id, string Hash, string Salt)>(plaintext.Count);
        foreach (var code in plaintext)
        {
            var normalized = MfaRecoveryCodes.Normalize(code);
            var (hash, salt) = _hasher.HashPassword(normalized);
            hashed.Add((Guid.NewGuid().ToString("N"), hash, salt));
        }

        return new NewCodes(plaintext, hashed, _hasher.AlgorithmName);
    }

    /// <summary>A generated set of recovery codes, owner-less until the operation names the account it loaded.</summary>
    private sealed record NewCodes(IReadOnlyList<string> Plaintext, IReadOnlyList<(string Id, string Hash, string Salt)> Hashed, string Algorithm)
    {
        public IReadOnlyList<NativeMfaRecoveryCodeRecord> For(UserId owner) =>
            Hashed.Select(c => new NativeMfaRecoveryCodeRecord(c.Id, owner, c.Hash, c.Salt, Algorithm, ConsumedAtUtc: null)).ToList();
    }
}
