using System.Security.Cryptography;
using System.Text;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

public sealed class AccountActionTokenService : IAccountActionTokenService
{
    private readonly IAccountActionTokenRepository _tokens;
    private readonly TimeProvider _time;

    public AccountActionTokenService(IAccountActionTokenRepository tokens, TimeProvider time)
    {
        _tokens = tokens;
        _time = time;
    }

    public async ValueTask<IssuedAccountActionToken> IssueAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        string? destinationEmail,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        await RevokeActiveAsync(userId, purpose, cancellationToken);
        var now = _time.GetUtcNow();
        var plaintext = CreatePlaintext();
        var record = new AccountActionTokenRecord(
            Guid.NewGuid().ToString("N")[..24],
            userId,
            purpose,
            Hash(plaintext),
            destinationEmail,
            now,
            now.Add(lifetime),
            ConsumedAtUtc: null,
            RevokedAtUtc: null);
        await _tokens.AddAsync(record, cancellationToken);
        return new IssuedAccountActionToken(plaintext, record.ExpiresAtUtc, destinationEmail);
    }

    public async ValueTask<AccountActionTokenRecord?> TryConsumeAsync(
        string plaintext,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return null;

        var record = await _tokens.FindByHashAsync(Hash(plaintext.Trim()), cancellationToken);
        var now = _time.GetUtcNow();
        if (record is null || record.Purpose != purpose || !record.IsActive(now))
            return null;

        var consumed = record with { ConsumedAtUtc = now };
        await _tokens.UpdateAsync(consumed, cancellationToken);
        return consumed;
    }

    public async ValueTask RevokeActiveAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var active = await _tokens.ListActiveAsync(userId, purpose, now, cancellationToken);
        foreach (var token in active)
            await _tokens.UpdateAsync(token with { RevokedAtUtc = now }, cancellationToken);
    }

    public async ValueTask RevokePasswordLifecycleTokensAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        await RevokeActiveAsync(userId, AccountActionTokenPurpose.PasswordSetup, cancellationToken);
        await RevokeActiveAsync(userId, AccountActionTokenPurpose.PasswordReset, cancellationToken);
    }

    internal static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string CreatePlaintext()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
