using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Connector;

public sealed record ConnectionVerificationResult(
    ConnectionVerificationStatus Status,
    DateTimeOffset VerifiedAtUtc,
    string Summary);

/// <summary>
/// Fixed, safe verification summaries. The summary is persisted on the Connection and shown to anyone who can read it, so
/// a failure that involves secret material is reported with these messages rather than the underlying exception text,
/// which can carry secret identifiers, provider paths or key names.
/// </summary>
public static class ConnectionVerificationMessages
{
    public const string SecretResolutionFailed =
        "Could not resolve this connection's stored secrets. Check that each secret field still has a stored value.";
}
