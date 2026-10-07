using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Forgotten-password requests and resets.
/// </summary>
/// <remarks>
/// AUD-1.2: the request is anonymous (corrects the possible subject-as-actor defect of gate §15.3). For an eligible account
/// the eligibility checks, the new reset link and its row commit together and the link is mailed once after commit with its
/// own W3 row. A request that matches no eligible account changes nothing and writes only a W3 row that names neither the
/// identifier nor an account. Using a link is one operation of the user it was issued to.
/// </remarks>
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
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly ILogger<ForgottenPasswordService>? _logger;

    public ForgottenPasswordService(
        IUserRepository users,
        ILocalCredentialRepository credentials,
        ILocalPasswordHasher hasher,
        IAccountActionTokenService tokens,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        ILogger<ForgottenPasswordService>? logger = null)
    {
        _logger = logger;
        _users = users;
        _credentials = credentials;
        _hasher = hasher;
        _tokens = tokens;
        _mail = mail;
        _links = links;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Password resets require the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Password resets require the audit event writer.");

    private static readonly AuditedOperation RequestOperation = new(AuditActionNames.PasswordResetRequested, AuditActor.Anonymous);

    public async ValueTask RequestAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var token = _tokens.Prepare();
        ResetRequest? staged = null;
        var issued = await Unit.ExecuteAsync(
            RequestOperation,
            async (transaction, ct) =>
            {
                staged = null;
                var user = await ResolveEligibleUserAsync(identifier, ct);
                if (user is null)
                {
                    transaction.DeclareNoOp();
                    return ResetRequest.NotEligible;
                }

                var link = await _tokens.IssueAsync(
                    user.Id,
                    AccountActionTokenPurpose.PasswordReset,
                    user.EmailAddress,
                    ResetLifetime,
                    token,
                    ct);
                await transaction.StageAsync(AuditEvents.PasswordResetRequested(user.Id, link.Id), ct);
                staged = new ResetRequest(user, link);
                return staged;
            },
            (committed, _) => Task.FromResult(
                staged?.Link is { } link && string.Equals(committed.GetField("requestId"), link.Id, StringComparison.Ordinal)
                    ? staged
                    // Committed, but the link cannot be shown to be the committed one: nothing is sent (D8). The caller's
                    // acknowledgement is the same; a new request issues a new link.
                    : ResetRequest.LinkUnavailable),
            cancellationToken);

        if (ReferenceEquals(issued, ResetRequest.NotEligible))
        {
            await Events.TryWriteAsync(AuditEvents.PasswordResetRequestSuppressed(), AuditActor.Anonymous, CancellationToken.None);
            return;
        }

        if (issued is not { User: { } user, Link: { } issuedLink })
            return;

        await AccountSecurityMailDelivery.SendAsync(
            _mail,
            Events,
            RequestOperation.Actor,
            user.Id,
            issuedLink.Id,
            AccountSecurityMailDelivery.PasswordReset,
            user.EmailAddress!,
            "Reset your Apperture password",
            $"Reset your Apperture local password by opening this one-time link:\n{_links.ResetPassword(issuedLink.Plaintext)}\n\nThis link expires and can be used once.",
            cancellationToken,
            _logger);
    }

    /// <summary>The outcome of a reset request's transaction. Never printed with its link.</summary>
    private sealed record ResetRequest(User? User, IssuedAccountActionToken? Link)
    {
        public static readonly ResetRequest NotEligible = new(null, null);
        public static readonly ResetRequest LinkUnavailable = new(null, null);

        public override string ToString() => $"ResetRequest {{ UserId = {User?.Id.Value}, RequestId = {Link?.Id} }}";
    }

    public async ValueTask CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        // The link names its user; possession of an active link is this flow's proof (AuthenticatedUser, D2).
        var link = await _tokens.FindActiveAsync(token, AccountActionTokenPurpose.PasswordReset, cancellationToken)
            ?? throw ResetLinkInvalid();
        // Hashed once, outside the transaction: a rerun stores the same hash.
        var (hash, salt) = _hasher.HashPassword(newPassword);

        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.PasswordResetCompleted, AuditActor.AuthenticatedUser(link.UserId)),
            async (transaction, ct) =>
            {
                // Every refusal comes before the consumption, so a refused attempt leaves the link usable.
                var user = await _users.GetByIdAsync(link.UserId, ct) ?? throw ResetLinkInvalid();
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                    throw ResetLinkInvalid();

                var cred = await _credentials.GetByUserIdAsync(user.Id, ct);
                if (cred is null || cred.IsDisabled || !user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
                    throw ResetLinkInvalid();

                RequirePassword(newPassword, user);
                var consumed = await _tokens.TryConsumeAsync(token, AccountActionTokenPurpose.PasswordReset, ct);
                if (consumed is null || consumed.Id != link.Id)
                    throw ResetLinkInvalid();

                await _credentials.SetPasswordAsync(user.Id, hash, salt, _hasher.AlgorithmName, ct);
                await _tokens.RevokePasswordLifecycleTokensAsync(user.Id, ct);
                await transaction.StageAsync(AuditEvents.PasswordResetCompleted(user.Id), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    private static AccountSecurityException ResetLinkInvalid() =>
        new(AccountSecurityErrorCodes.TokenInvalid, "This reset link is invalid or has expired.");

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
        return cred is { IsDisabled: false } ? user : null;
    }

    private static void RequirePassword(string newPassword, User user)
    {
        try
        {
            LocalPasswordPolicy.Require(newPassword, user);
        }
        catch (PasswordPolicyException ex)
        {
            throw new AccountSecurityException(AccountSecurityErrorCodes.PasswordRequired, ex.Message);
        }
    }
}
