using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Accounts;

public enum AccountActionTokenPurpose
{
    VerifyContactEmail = 1,
    PasswordSetup = 2,
    PasswordReset = 3
}

public sealed record AccountActionTokenRecord(
    string Id,
    UserId UserId,
    AccountActionTokenPurpose Purpose,
    string TokenHash,
    string? DestinationEmail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc)
{
    public bool IsActive(DateTimeOffset now) =>
        ConsumedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;
}

public sealed record IssuedAccountActionToken(
    string Plaintext,
    DateTimeOffset ExpiresAtUtc,
    string? DestinationEmail);

public interface IAccountActionTokenRepository
{
    ValueTask AddAsync(AccountActionTokenRecord token, CancellationToken cancellationToken = default);

    ValueTask<AccountActionTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AccountActionTokenRecord>> ListActiveAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<AccountActionTokenRecord?> FindLatestAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(AccountActionTokenRecord token, CancellationToken cancellationToken = default);
}

public interface IAccountActionTokenService
{
    ValueTask<IssuedAccountActionToken> IssueAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        string? destinationEmail,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    ValueTask<AccountActionTokenRecord?> TryConsumeAsync(
        string plaintext,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default);

    ValueTask RevokeActiveAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default);

    ValueTask RevokePasswordLifecycleTokensAsync(UserId userId, CancellationToken cancellationToken = default);
}
