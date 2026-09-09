using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class AccountContactService : IAccountContactService
{
    internal static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly IAccountActionTokenService _tokens;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;
    private readonly IAuditService _audit;
    private readonly IStrongAuthenticationGate? _strongAuth;
    private readonly IRecentAccountVerificationGate? _accountVerification;

    public AccountContactService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IAuditService audit,
        IStrongAuthenticationGate? strongAuth = null,
        IRecentAccountVerificationGate? accountVerification = null)
    {
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _mail = mail;
        _links = links;
        _audit = audit;
        _strongAuth = strongAuth;
        _accountVerification = accountVerification;
    }

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
        var user = await RequireOrdinaryUserAsync(userId, cancellationToken);
        var normalized = ContactEmailAddress.Normalize(email)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "Enter a valid contact email address.");

        if (user.HasVerifiedContactEmail
            && string.Equals(user.EmailAddress, normalized, StringComparison.Ordinal))
        {
            return new AccountContactMutationResult(await SnapshotAsync(user, cancellationToken), true, null);
        }

        if (user.HasVerifiedContactEmail)
            await RequireChangeProofAsync(user, currentPassword, cancellationToken);

        var owner = await _users.FindByVerifiedContactEmailAsync(normalized, cancellationToken);
        if (owner is not null && owner.Id != user.Id)
            throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

        var changing = user.HasVerifiedContactEmail;
        var updated = user with { PendingEmailAddress = normalized };
        await _users.UpdateAsync(updated, cancellationToken);

        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.VerifyContactEmail,
            normalized,
            VerificationLifetime,
            cancellationToken);
        var delivery = await SendVerificationAsync(normalized, issued.Plaintext, cancellationToken);
        await AuditAsync(
            changing ? AccountSecurityAuditActionNames.EmailChangeRequested : AccountSecurityAuditActionNames.EmailVerificationRequested,
            user.Id,
            $"userId={user.Id.Value} pending=1 delivered={delivery.Succeeded}",
            cancellationToken);
        if (!delivery.Succeeded)
            await AuditAsync(AccountSecurityAuditActionNames.DeliveryFailed, user.Id, $"userId={user.Id.Value} purpose=verify", cancellationToken);

        return new AccountContactMutationResult(await SnapshotAsync(updated, cancellationToken), delivery.Succeeded, delivery.FailureSummary);
    }

    public async ValueTask<AccountContactMutationResult> ResendAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireOrdinaryUserAsync(userId, cancellationToken);
        var pending = user.PendingEmailAddress
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "No verification is pending.");

        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.VerifyContactEmail,
            pending,
            VerificationLifetime,
            cancellationToken);
        var delivery = await SendVerificationAsync(pending, issued.Plaintext, cancellationToken);
        await AuditAsync(
            AccountSecurityAuditActionNames.EmailVerificationRequested,
            user.Id,
            $"userId={user.Id.Value} resend=1 delivered={delivery.Succeeded}",
            cancellationToken);
        return new AccountContactMutationResult(await SnapshotAsync(user, cancellationToken), delivery.Succeeded, delivery.FailureSummary);
    }

    public async ValueTask VerifyAsync(string token, CancellationToken cancellationToken = default)
    {
        var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.VerifyContactEmail, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This verification link is invalid or has expired.");

        var user = await _users.GetByIdAsync(consumed.UserId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This verification link is invalid or has expired.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.SystemManaged, "Recovery Access does not use contact email.");

        var destination = ContactEmailAddress.Normalize(consumed.DestinationEmail ?? user.PendingEmailAddress);
        if (destination is null)
            throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This verification link is invalid or has expired.");

        var owner = await _users.FindByVerifiedContactEmailAsync(destination, cancellationToken);
        if (owner is not null && owner.Id != user.Id)
            throw new AccountSecurityException(AccountSecurityErrorCodes.EmailInUse, "That contact email cannot be used.");

        var changing = user.HasVerifiedContactEmail
            && !string.Equals(user.EmailAddress, destination, StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var updated = user with
        {
            EmailAddress = destination,
            EmailVerifiedAtUtc = now,
            PendingEmailAddress = null
        };
        await _users.UpdateAsync(updated, cancellationToken);
        await AuditAsync(
            changing ? AccountSecurityAuditActionNames.EmailChanged : AccountSecurityAuditActionNames.EmailVerified,
            user.Id,
            $"userId={user.Id.Value}",
            cancellationToken);
    }

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

    private async ValueTask<AccountSecurityMailResult> SendVerificationAsync(
        string email,
        string plaintext,
        CancellationToken cancellationToken)
    {
        var link = _links.VerifyEmail(plaintext);
        return await _mail.SendAsync(
            email,
            "Verify your Apperture contact email",
            $"Confirm this address as your Apperture contact email by opening:\n{link}\n\nThis link expires and can be used once.",
            cancellationToken);
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
