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

/// <summary>
/// A one-time token issued into the current transaction: <paramref name="Id"/> is the row id (recorded in audit rows),
/// <paramref name="Plaintext"/> exists only in memory for the link that is mailed after commit.
/// </summary>
public sealed record IssuedAccountActionToken(
    string Id,
    string Plaintext,
    DateTimeOffset ExpiresAtUtc,
    string? DestinationEmail)
{
    /// <summary>Never prints the plaintext.</summary>
    public override string ToString() => $"IssuedAccountActionToken {{ Id = {Id}, ExpiresAtUtc = {ExpiresAtUtc:O} }}";
}

/// <summary>
/// A token's id and plaintext, chosen once before an audited unit of work (AUD-1.2). Every attempt of the unit issues
/// this same token, so a rerun after a confirmed rollback never mints a second link, and the plaintext held after commit
/// belongs to exactly the committed row. Never recorded, logged or reconstructed.
/// </summary>
public sealed record PreparedAccountActionToken(string Id, string Plaintext)
{
    /// <summary>Never prints the plaintext.</summary>
    public override string ToString() => $"PreparedAccountActionToken {{ Id = {Id} }}";
}

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
    /// <summary>Chooses a new token's id and plaintext without storing anything.</summary>
    PreparedAccountActionToken Prepare();

    /// <summary>Revokes the user's active tokens of <paramref name="purpose"/> and stores <paramref name="prepared"/>'s hash.</summary>
    ValueTask<IssuedAccountActionToken> IssueAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        string? destinationEmail,
        TimeSpan lifetime,
        PreparedAccountActionToken prepared,
        CancellationToken cancellationToken = default);

    /// <summary>The active token of <paramref name="purpose"/> matching <paramref name="plaintext"/>, without consuming it.</summary>
    ValueTask<AccountActionTokenRecord?> FindActiveAsync(
        string plaintext,
        AccountActionTokenPurpose purpose,
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
