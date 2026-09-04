using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class ForgottenPasswordService : IForgottenPasswordService
{
    internal static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    public const string GenericAcknowledgement = "If an eligible account matches, a reset email is on its way.";

    private readonly IUserRepository _users;
    private readonly ILocalCredentialRepository _credentials;
    private readonly ILocalPasswordHasher _hasher;
    private readonly IAccountActionTokenService _tokens;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;
    private readonly IAuditService _audit;
    private readonly IOptions<LocalAuthOptions> _local;

    public ForgottenPasswordService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IAuditService audit,
        IOptions<LocalAuthOptions> local)
    {
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _mail = mail;
        _links = links;
        _audit = audit;
        _local = local;
    }

    public async ValueTask RequestAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var user = await ResolveEligibleUserAsync(identifier, cancellationToken);
        if (user is null)
        {
            await AuditAsync(
                AccountSecurityAuditActionNames.PasswordResetRequested,
                new ActorId("anonymous"),
                "suppressed",
                cancellationToken);
            return;
        }

        var issued = await _tokens.IssueAsync(
            user.Id,
            AccountActionTokenPurpose.PasswordReset,
            user.EmailAddress,
            ResetLifetime,
            cancellationToken);
        var delivery = await _mail.SendAsync(
            user.EmailAddress!,
            "Reset your Apperture password",
            $"Reset your Apperture local password by opening this one-time link:\n{_links.ResetPassword(issued.Plaintext)}\n\nThis link expires and can be used once. It will not turn local sign-in on if it is currently disabled.",
            cancellationToken);
        await AuditAsync(
            AccountSecurityAuditActionNames.PasswordResetRequested,
            new ActorId(user.Id.Value),
            $"userId={user.Id.Value} delivered={delivery.Succeeded}",
            cancellationToken);
        if (!delivery.Succeeded)
        {
            await AuditAsync(
                AccountSecurityAuditActionNames.DeliveryFailed,
                new ActorId(user.Id.Value),
                $"userId={user.Id.Value} purpose=reset",
                cancellationToken);
        }
    }

    public async ValueTask CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.PasswordReset, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This reset link is invalid or has expired.");

        var user = await _users.GetByIdAsync(consumed.UserId, cancellationToken)
            ?? throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This reset link is invalid or has expired.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
            throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This reset link is invalid or has expired.");

        var cred = await _credentials.GetByUserIdAsync(user.Id, cancellationToken);
        if (cred is null || !user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new AccountSecurityException(AccountSecurityErrorCodes.TokenInvalid, "This reset link is invalid or has expired.");

        ValidateNewPassword(newPassword);
        var (hash, salt) = _hasher.HashPassword(newPassword);
        await _credentials.SetPasswordAsync(user.Id, hash, salt, _hasher.AlgorithmName, cancellationToken);
        await _tokens.RevokePasswordLifecycleTokensAsync(user.Id, cancellationToken);
        await AuditAsync(
            AccountSecurityAuditActionNames.PasswordResetCompleted,
            new ActorId(user.Id.Value),
            $"userId={user.Id.Value} localRemainedDisabled={cred.IsDisabled}",
            cancellationToken);
    }

    private async ValueTask<User?> ResolveEligibleUserAsync(string identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var trimmed = identifier.Trim();
        User? user = null;
        var asEmail = ContactEmailAddress.Normalize(trimmed);
        if (asEmail is not null)
            user = await _users.FindByVerifiedContactEmailAsync(asEmail, cancellationToken);

        user ??= await _users.GetByIdentityLinkAsync(IdentitySource.Local, LocalAuthHelpers.NormalizeUsername(trimmed), cancellationToken);
        if (user is null || EmergencyAccessIdentity.IsSystemManaged(user))
            return null;
        if (!user.HasVerifiedContactEmail)
            return null;
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            return null;

        var cred = await _credentials.GetByUserIdAsync(user.Id, cancellationToken);
        return cred is null ? null : user;
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

    private async ValueTask AuditAsync(string action, ActorId actorId, string details, CancellationToken cancellationToken)
    {
        await _audit.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                actorId,
                action,
                Resource: null,
                AccountSecurityAuditActionNames.Outcome,
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                details),
            cancellationToken);
    }
}
