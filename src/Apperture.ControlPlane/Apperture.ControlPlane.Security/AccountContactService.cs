using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Self-service contact email.
/// </summary>
/// <remarks>
/// AUD-1.2: a request (or resend) is one audited operation of the signed-in user: the safety checks, the pending address, the
/// new verification link and its row commit together, and the link is mailed once after commit with its own W3 row. Asking
/// for the address already verified changes nothing and writes no row. Using a link is one operation of the user it was
/// issued to.
/// </remarks>
public sealed class AccountContactService : IAccountContactService
{
    internal static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly IAccountActionTokenService _tokens;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;
    private readonly IStrongAuthenticationGate? _strongAuth;
    private readonly IRecentAccountVerificationGate? _accountVerification;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public AccountContactService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IStrongAuthenticationGate? strongAuth = null,
        IRecentAccountVerificationGate? accountVerification = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _mail = mail;
        _links = links;
        _strongAuth = strongAuth;
        _accountVerification = accountVerification;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Contact email changes require the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Contact email changes require the audit event writer.");

    private static readonly AuditedOperation RequestOperation = new(
        AuditActionNames.EmailVerificationRequested,
        AuditActor.CurrentPrincipal,
        [AuditActionNames.EmailChangeRequested]);

    public async ValueTask<AccountContactSnapshot> GetAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "Account was not found.");
        return await SnapshotAsync(user, cancellationToken);
    }

    public async ValueTask<AccountContactMutationResult> RequestAsync(
        UserId userId,
        string email,
        string? currentPassword,
        CancellationToken cancellationToken = default)
    {
        var normalized = ContactEmailAddress.Normalize(email);
        var token = _tokens.Prepare();
        IssuedVerification? staged = null;
        var issued = await Unit.ExecuteAsync<IssuedVerification?>(
            RequestOperation,
            async (transaction, ct) =>
            {
                staged = null;
                var user = await RequireOrdinaryUserAsync(userId, ct);
                if (normalized is null)
                    throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "Enter a valid contact email address.");

                if (user.HasVerifiedContactEmail && string.Equals(user.EmailAddress, normalized, StringComparison.Ordinal))
                {
                    // Already the verified address: nothing changes and the response is the current state (D7).
                    transaction.DeclareNoOp();
                    return new IssuedVerification(user, null);
                }

                var changing = user.HasVerifiedContactEmail;
                if (changing)
                    await RequireChangeProofAsync(user, currentPassword, ct);
                else
                    RequireRecentAccountVerification();

                var owner = await _users.FindByVerifiedContactEmailAsync(normalized, ct);
                if (owner is not null && owner.Id != user.Id)
                    throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

                var updated = user with { PendingEmailAddress = normalized };
                await _users.UpdateAsync(updated, ct);
                staged = await IssueAsync(transaction, updated, normalized, token, changing, resent: false, ct);
                return staged;
            },
            (committed, _) => Task.FromResult(Reconstructed(committed, staged)),
            cancellationToken);

        return await CompleteAsync(userId, issued, cancellationToken);
    }

    public async ValueTask<AccountContactMutationResult> ResendAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        RequireRecentAccountVerification();
        var token = _tokens.Prepare();
        IssuedVerification? staged = null;
        var issued = await Unit.ExecuteAsync<IssuedVerification?>(
            RequestOperation,
            async (transaction, ct) =>
            {
                staged = null;
                var user = await RequireOrdinaryUserAsync(userId, ct);
                var pending = user.PendingEmailAddress
                    ?? throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "No verification is pending.");
                staged = await IssueAsync(transaction, user, pending, token, user.HasVerifiedContactEmail, resent: true, ct);
                return staged;
            },
            (committed, _) => Task.FromResult(Reconstructed(committed, staged)),
            cancellationToken);

        return await CompleteAsync(userId, issued, cancellationToken);
    }

    public async ValueTask VerifyAsync(string token, CancellationToken cancellationToken = default)
    {
        // The link names its user; possession of an active link is this flow's proof (AuthenticatedUser, D2).
        var link = await _tokens.FindActiveAsync(token, AccountActionTokenPurpose.VerifyContactEmail, cancellationToken)
            ?? throw VerificationLinkInvalid();

        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.EmailVerified, AuditActor.AuthenticatedUser(link.UserId), [AuditActionNames.EmailChanged]),
            async (transaction, ct) =>
            {
                // Every refusal comes before the consumption, so a refused attempt leaves the link usable.
                var user = await _users.GetByIdAsync(link.UserId, ct) ?? throw VerificationLinkInvalid();
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                    throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access does not use contact email.");

                var destination = ContactEmailAddress.Normalize(link.DestinationEmail ?? user.PendingEmailAddress)
                    ?? throw VerificationLinkInvalid();
                var owner = await _users.FindByVerifiedContactEmailAsync(destination, ct);
                if (owner is not null && owner.Id != user.Id)
                    throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

                var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.VerifyContactEmail, ct);
                if (consumed is null || consumed.Id != link.Id)
                    throw VerificationLinkInvalid();

                var changing = user.HasVerifiedContactEmail
                    && !string.Equals(user.EmailAddress, destination, StringComparison.Ordinal);
                await _users.UpdateAsync(
                    user with
                    {
                        EmailAddress = destination,
                        EmailVerifiedAtUtc = DateTimeOffset.UtcNow,
                        PendingEmailAddress = null
                    },
                    ct);
                await transaction.StageAsync(
                    changing ? AuditEvents.EmailChanged(user.Id) : AuditEvents.EmailVerified(user.Id, viaSetupInvitation: false),
                    ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    /// <summary>A verification link issued in a transaction: the user as updated, and the link (null for a no-op).</summary>
    private sealed record IssuedVerification(User User, IssuedAccountActionToken? Link)
    {
        public override string ToString() => $"IssuedVerification {{ UserId = {User.Id.Value}, RequestId = {Link?.Id} }}";
    }

    private async Task<IssuedVerification> IssueAsync(
        IAuditedTransaction transaction,
        User user,
        string destination,
        PreparedAccountActionToken token,
        bool changing,
        bool resent,
        CancellationToken cancellationToken)
    {
        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.VerifyContactEmail,
            destination,
            VerificationLifetime,
            token,
            cancellationToken);
        await transaction.StageAsync(AuditEvents.ContactVerificationRequested(user.Id, issued.Id, changing, resent), cancellationToken);
        return new IssuedVerification(user, issued);
    }

    /// <summary>
    /// The committed request's link, only when it is the one this request issued (the token is chosen once, so it always is
    /// when the commit was verified). Otherwise null: nothing is sent (D8).
    /// </summary>
    private static IssuedVerification? Reconstructed(AuditCommittedOperation committed, IssuedVerification? staged) =>
        staged?.Link is { } link && string.Equals(committed.GetField("requestId"), link.Id, StringComparison.Ordinal)
            ? staged
            : null;

    private async ValueTask<AccountContactMutationResult> CompleteAsync(
        UserId userId,
        IssuedVerification? issued,
        CancellationToken cancellationToken)
    {
        if (issued is null)
        {
            // Committed, but the link cannot be shown to be the committed one: report the committed state, send nothing.
            var current = await RequireOrdinaryUserAsync(userId, cancellationToken);
            return new AccountContactMutationResult(
                await SnapshotAsync(current, cancellationToken),
                false,
                AccountSecurityMailDelivery.LinkUnavailable);
        }

        if (issued.Link is null)
            return new AccountContactMutationResult(await SnapshotAsync(issued.User, cancellationToken), true, null);

        var delivery = await AccountSecurityMailDelivery.SendAsync(
            _mail,
            Events,
            RequestOperation.Actor,
            issued.User.Id,
            issued.Link.Id,
            AccountSecurityMailDelivery.ContactVerification,
            issued.Link.DestinationEmail!,
            "Verify your Apperture contact email",
            $"Confirm this address as your Apperture contact email by opening:\n{_links.VerifyEmail(issued.Link.Plaintext)}\n\nThis link expires and can be used once.",
            cancellationToken);
        return new AccountContactMutationResult(await SnapshotAsync(issued.User, cancellationToken), delivery.Succeeded, delivery.FailureSummary);
    }

    private static AccountSecurityException VerificationLinkInvalid() =>
        new(AccountSecurityErrorCodes.TokenInvalid, "This verification link is invalid or has expired.");

    private void RequireRecentAccountVerification() => _accountVerification?.Require();

    private async ValueTask RequireChangeProofAsync(User user, string? currentPassword, CancellationToken cancellationToken)
    {
        if (_accountVerification?.GetCurrent().IsSatisfied == true)
            return;

        var cred = await _credentials.GetByUserIdAsync(user.Id, cancellationToken);
        if (cred is { IsDisabled: false })
        {
            if (string.IsNullOrEmpty(currentPassword)
                || !_hasher.VerifyPassword(currentPassword, cred.PasswordHash, cred.Salt))
            {
                throw new AccountSecurityException(
                    AccountSecurityErrorCodes.CurrentPasswordInvalid,
                    "Confirm your current local password to change a verified contact email.");
            }

            return;
        }

        if (_strongAuth is null || !user.IdentityLinks.Any(l => l.Source == IdentitySource.Entra))
            return;

        try
        {
            await _strongAuth.RequireRecentStrongAuthenticationAsync(cancellationToken);
        }
        catch (StrongAuthenticationException ex) when (ex.Code == StrongAuthenticationErrorCodes.NotConfigured)
        {
            // Entra session is the strongest available proof when Authentication Context is not configured.
        }
    }

    private async ValueTask<User> RequireOrdinaryUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "Account was not found.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access does not use contact email.");
        return user;
    }

    private async ValueTask<AccountContactSnapshot> SnapshotAsync(User user, CancellationToken cancellationToken)
    {
        var cred = await _credentials.GetByUserIdAsync(user.Id, cancellationToken);
        var entra = user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Entra);
        var suggested = ContactEmailAddress.Normalize(entra?.MailSnapshot)
            ?? ContactEmailAddress.Normalize(entra?.UserPrincipalNameSnapshot);
        return new AccountContactSnapshot(
            user.EmailAddress,
            user.ContactEmailStatus,
            user.EmailVerifiedAtUtc,
            user.PendingEmailAddress,
            suggested,
            CanManage: !EmergencyAccessIdentity.IsSystemManaged(user),
            EmergencyAccessAccount: EmergencyAccessIdentity.IsSystemManaged(user),
            HasEnabledLocalPassword: cred is { IsDisabled: false });
    }
}
