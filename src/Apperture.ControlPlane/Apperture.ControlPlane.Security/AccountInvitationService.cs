using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Apperture account setup invitations.
/// </summary>
/// <remarks>
/// AUD-1.2: issuing an invitation is one audited operation of the administrator (the invited user is the target, correcting
/// gate §15.3): the pending address, the new token, the revocation of earlier ones and the sent or resent row commit together.
/// The link is mailed once, after commit, and the send has its own W3 row. Using an invitation is one operation of the user the
/// link was issued to: consumption, the password, revocation of the other password links, the verified address and their
/// rows commit together.
/// </remarks>
public sealed class AccountInvitationService : IAccountInvitationService
{
    internal static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(72);

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly IAccountActionTokenService _tokens;
    private readonly IAccountActionTokenRepository _tokenRows;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;
    private readonly TimeProvider _time;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly ILogger<AccountInvitationService>? _logger;

    public AccountInvitationService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountActionTokenRepository tokenRows,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        TimeProvider time,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        ILogger<AccountInvitationService>? logger = null)
    {
        _logger = logger;
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _tokenRows = tokenRows;
        _mail = mail;
        _links = links;
        _time = time;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Setup invitations require the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Setup invitations require the audit event writer.");

    private static readonly AuditedOperation IssueOperation = new(
        AuditActionNames.InvitationSent,
        AuditActor.CurrentPrincipal,
        [AuditActionNames.InvitationResent]);

    public async ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string? contactEmail,
        CancellationToken cancellationToken = default)
    {
        // Chosen once: every attempt issues this token, so the link held after commit is the committed one.
        var token = _tokens.Prepare();
        PreparedSetupInvitation? staged = null;
        var invitation = await Unit.ExecuteAsync<PreparedSetupInvitation?>(
            IssueOperation,
            async (transaction, ct) =>
            {
                staged = await StageSetupInvitationAsync(transaction, userId, contactEmail, token, ct);
                return staged;
            },
            (committed, _) => Task.FromResult(
                staged is not null && string.Equals(committed.GetField("requestId"), staged.Token.Id, StringComparison.Ordinal)
                    ? staged
                    : null),
            cancellationToken);

        if (invitation is null)
        {
            // Verified committed, but the link in hand cannot be shown to be the committed one: send nothing (D8).
            return new AccountInvitationResult(userId, false, AccountSecurityMailDelivery.LinkUnavailable, _time.GetUtcNow());
        }

        return await DeliverSetupInvitationAsync(invitation, IssueOperation.Actor, cancellationToken);
    }

    public async ValueTask<PreparedSetupInvitation> StageSetupInvitationAsync(
        IAuditedTransaction transaction,
        UserId userId,
        string? contactEmail,
        PreparedAccountActionToken token,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId.Value} not found.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access cannot receive an onboarding invitation.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new AccountSecurityException(AccountSecurityErrorCodes.InvitationRequired, "A local username is required for an Apperture setup invitation.");

        var normalized = ContactEmailAddress.Normalize(contactEmail ?? user.PendingEmailAddress ?? user.EmailAddress)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "A contact email is required for the setup invitation.");

        var owner = await _users.FindByVerifiedContactEmailAsync(normalized, cancellationToken);
        if (owner is not null && owner.Id != user.Id)
            throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

        var resent = await _tokenRows.FindLatestAsync(user.Id, AccountActionTokenPurpose.PasswordSetup, cancellationToken) is not null;
        if (!string.Equals(user.PendingEmailAddress, normalized, StringComparison.Ordinal))
            await _users.UpdateAsync(user with { PendingEmailAddress = normalized }, cancellationToken);

        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.PasswordSetup,
            normalized,
            InvitationLifetime,
            token,
            cancellationToken);
        await transaction.StageAsync(
            resent ? AuditEvents.InvitationResent(user.Id, issued.Id) : AuditEvents.InvitationSent(user.Id, issued.Id),
            cancellationToken);
        return new PreparedSetupInvitation(user.Id, normalized, issued);
    }

    public async ValueTask<AccountInvitationResult> DeliverSetupInvitationAsync(
        PreparedSetupInvitation invitation,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var delivery = await AccountSecurityMailDelivery.SendAsync(
            _mail,
            Events,
            actor,
            invitation.UserId,
            invitation.Token.Id,
            AccountSecurityMailDelivery.SetupInvitation,
            invitation.Destination,
            "Set up your Apperture account",
            $"An administrator invited you to choose your Apperture password. Open this one-time link:\n{_links.SetupPassword(invitation.Token.Plaintext)}\n\nThis invitation expires and can be used once.",
            cancellationToken,
            _logger);
        return new AccountInvitationResult(invitation.UserId, delivery.Succeeded, delivery.FailureSummary, invitation.Token.ExpiresAtUtc);
    }

    public async ValueTask CompletePasswordSetupAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        RequirePassword(newPassword, user: null);

        // The link names its user; possession of an active link is this flow's proof (AuthenticatedUser, D2). Everything the
        // decision depends on is read again inside the transaction.
        var link = await _tokens.FindActiveAsync(token, AccountActionTokenPurpose.PasswordSetup, cancellationToken)
            ?? throw SetupLinkInvalid();
        var (hash, salt) = _hasher.HashPassword(newPassword);

        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.PasswordSetupCompleted, AuditActor.AuthenticatedUser(link.UserId)),
            async (transaction, ct) =>
            {
                // Every refusal comes before the consumption, so a refused attempt leaves the link usable.
                var user = await _users.GetByIdAsync(link.UserId, ct) ?? throw SetupLinkInvalid();
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                    throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access cannot use an onboarding invitation.");
                RequirePassword(newPassword, user);
                if (await _credentials.GetByUserIdAsync(user.Id, ct) is not null)
                    throw SetupLinkInvalid();

                var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.PasswordSetup, ct);
                if (consumed is null || consumed.Id != link.Id)
                    throw SetupLinkInvalid();

                await _credentials.SetPasswordAsync(user.Id, hash, salt, _hasher.AlgorithmName, ct);
                await _tokens.RevokePasswordLifecycleTokensAsync(user.Id, ct);
                await transaction.StageAsync(AuditEvents.PasswordSetupCompleted(user.Id), ct);
                await transaction.StageAsync(AuditEvents.InvitationAccepted(user.Id, consumed.Id), ct);

                var destination = ContactEmailAddress.Normalize(consumed.DestinationEmail ?? user.PendingEmailAddress);
                if (destination is not null)
                {
                    var owner = await _users.FindByVerifiedContactEmailAsync(destination, ct);
                    if (owner is null || owner.Id == user.Id)
                    {
                        var alreadyVerified = user.HasVerifiedContactEmail
                            && string.Equals(user.EmailAddress, destination, StringComparison.Ordinal);
                        var replacing = user.HasVerifiedContactEmail && !alreadyVerified;
                        await _users.UpdateAsync(
                            user with
                            {
                                EmailAddress = destination,
                                EmailVerifiedAtUtc = alreadyVerified ? user.EmailVerifiedAtUtc : _time.GetUtcNow(),
                                PendingEmailAddress = null
                            },
                            ct);
                        if (!alreadyVerified)
                        {
                            await transaction.StageAsync(
                                replacing ? AuditEvents.EmailChanged(user.Id) : AuditEvents.EmailVerified(user.Id, viaSetupInvitation: true),
                                ct);
                        }
                    }
                }

                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    public async ValueTask<AccountInvitationStatus?> GetStatusAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var latest = await _tokenRows.FindLatestAsync(userId, AccountActionTokenPurpose.PasswordSetup, cancellationToken);
        if (latest is null)
            return null;

        var now = _time.GetUtcNow();
        return new AccountInvitationStatus(
            Pending: latest.IsActive(now),
            Expired: latest.ConsumedAtUtc is null && !latest.IsActive(now),
            latest.ExpiresAtUtc,
            latest.DestinationEmail,
            LastDeliveryFailed: false,
            LastDeliveryFailure: null);
    }

    private static AccountSecurityException SetupLinkInvalid() =>
        new(AccountSecurityErrorCodes.TokenInvalid, "This setup link is invalid or has expired.");

    private static void RequirePassword(string newPassword, User? user)
    {
        try
        {
            if (user is null)
                LocalPasswordPolicy.Require(newPassword);
            else
                LocalPasswordPolicy.Require(newPassword, user);
        }
        catch (PasswordPolicyException ex)
        {
            throw new AccountSecurityException(AccountSecurityErrorCodes.PasswordRequired, ex.Message);
        }
    }
}
