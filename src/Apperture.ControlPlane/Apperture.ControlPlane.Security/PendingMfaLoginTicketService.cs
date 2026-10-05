using System.Text;
using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class PendingMfaLoginTicketService : IPendingMfaLoginTicketService
{
    public const string ProtectorKeyId = "apperture.native-mfa.pending-login";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ISecretProtector _protector;
    private readonly TimeProvider _time;

    public PendingMfaLoginTicketService(ISecretProtector protector, TimeProvider time)
    {
        _protector = protector;
        _time = time;
    }

    public async ValueTask<string> IssueAsync(UserId userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new TicketPayload(userId.Value, utcNow.Add(Lifetime)));
        var protectedBytes = await _protector.ProtectAsync(Encoding.UTF8.GetBytes(payload), ProtectorKeyId, cancellationToken);
        return Convert.ToBase64String(protectedBytes);
    }

    public async ValueTask<PendingMfaLoginTicket?> RedeemAsync(string token, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[] plaintext;
        try
        {
            plaintext = await _protector.UnprotectAsync(protectedBytes, ProtectorKeyId, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }

        TicketPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TicketPayload>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.UserId) || utcNow >= payload.ExpiresUtc)
            return null;

        return new PendingMfaLoginTicket(new UserId(payload.UserId), payload.ExpiresUtc);
    }

    private sealed record TicketPayload(string UserId, DateTimeOffset ExpiresUtc);
}
