using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

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
    private readonly IAuditService _audit;
    private readonly IOptions<LocalAuthOptions> _local;
    private readonly TimeProvider _time;

    public AccountInvitationService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountActionTokenRepository tokenRows,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IAuditService audit,
        IOptions<LocalAuthOptions> local,
        TimeProvider time)
    {
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _tokenRows = tokenRows;
        _mail = mail;
        _links = links;
        _audit = audit;
        _local = local;
        _time = time;
    }

    public async ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "User was not found.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access cannot receive an onboarding invitation.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new AccountSecurityException(AccountSecurityErrorCodes.InvitationRequired, "A local username is required for an Apperture setup invitation.");

        var normalized = ContactEmailAddress.Normalize(contactEmail)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "A contact email is required for the setup invitation.");

        var owner = await _users.FindByVerifiedContactEmailAsync(normalized, cancellationToken);
        if (owner is not null && owner.Id != user.Id)
            throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

        var latest = await _tokenRows.FindLatestAsync(user.Id, AccountActionTokenPurpose.PasswordSetup, cancellationToken);
        var resent = latest is not null;
        var updated = user with { PendingEmailAddress = normalized };
        await _users.UpdateAsync(updated, cancellationToken);

        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.PasswordSetup,
            normalized,
            InvitationLifetime,
            cancellationToken);
        var delivery = await _mail.SendAsync(
            normalized,
            "Set up your Apperture account",
            $"An administrator invited you to choose your Apperture password. Open this one-time link:\n{_links.SetupPassword(issued.Plaintext)}\n\nThis invitation expires and can be used once.",
            cancellationToken);

        await AuditAsync(
            resent ? AccountSecurityAuditActionNames.InvitationResent : AccountSecurityAuditActionNames.InvitationSent,
            user.Id,
            $"userId={user.Id.Value} delivered={delivery.Succeeded}",
            cancellationToken);
        if (!delivery.Succeeded)
            await AuditAsync(AccountSecurityAuditActionNames.DeliveryFailed, user.Id, $"userId={user.Id.Value} purpose=invitation", cancellationToken);

        return new AccountInvitationResult(user.Id, delivery.Succeeded, delivery.FailureSummary, issued.ExpiresAtUtc);
    }

    public async ValueTask CompletePasswordSetupAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(newPassword);
        var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.PasswordSetup, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This setup link is invalid or has expired.");

        var user = await _users.GetByIdAsync(consumed.UserId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This setup link is invalid or has expired.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access cannot use an onboarding invitation.");
        var existing = await _credentials.GetByUserIdAsync(user.Id, cancellationToken);
        if (existing is not null)
            throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This setup link is invalid or has expired.");

        var (hash, salt) = _hasher.HashPassword(newPassword);
        await _credentials.SetPasswordAsync(user.Id, hash, salt, _hasher.AlgorithmName, cancellationToken);
        await _tokens.RevokePasswordLifecycleTokensAsync(user.Id, cancellationToken);

        var destination = ContactEmailAddress.Normalize(consumed.DestinationEmail ?? user.PendingEmailAddress);
        if (destination is not null)
        {
            var owner = await _users.FindByVerifiedContactEmailAsync(destination, cancellationToken);
            if (owner is null || owner.Id == user.Id)
            {
                await _users.UpdateAsync(
                    user with
                    {
                        EmailAddress = destination,
                        EmailVerifiedAtUtc = _time.GetUtcNow(),
                        PendingEmailAddress = null
                    },
                    cancellationToken);
                await AuditAsync(AccountSecurityAuditActionNames.EmailVerified, user.Id, $"userId={user.Id.Value} via=invitation", cancellationToken);
            }
        }

        await AuditAsync(AccountSecurityAuditActionNames.PasswordSetupCompleted, user.Id, $"userId={user.Id.Value}", cancellationToken);
        await AuditAsync(AccountSecurityAuditActionNames.InvitationAccepted, user.Id, $"userId={user.Id.Value}", cancellationToken);
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

    private void ValidateNewPassword(string newPassword)
    {
        var min = _local.Value.MinPasswordLength;
        if (string.IsNullOrEmpty(newPassword) || (min > 0 && newPassword.Length < min))
        {
            throw new AccountSecurityException(
                AccountSecurityErrorCodes.PasswordRequired,
                min > 0 ? $"Password must be at least {min} characters." : "A password is required.");
        }
    }

    private async ValueTask AuditAsync(string action, UserId userId, string details, CancellationToken cancellationToken)
    {
        await _audit.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId(userId.Value),
                action,
                Resource: null,
                AccountSecurityAuditActionNames.Outcome,
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                details),
            cancellationToken);
    }
}
