using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Application.Connector;

/// <summary>
/// The one definition of which secret a connection field owns: the store holds it under provider <c>platform-connection</c>
/// with exactly the name <c>connection:{connectionId}:{fieldKey}</c>. The rule that creates a connection's secrets, the rule that
/// proves a reference is the connection's own before it is rotated, and the rule every resolution applies before a secret is
/// read all come from here, so the write side and the read side cannot drift apart.
/// </summary>
/// <remarks>
/// A well-formed GUID in <c>Connection.SecretRefs</c> is not proof of ownership. The proof is the secret's own stored
/// descriptor, compared exactly (ordinal) with <see cref="For"/>.
/// </remarks>
public static class ConnectionSecretOwnership
{
    /// <summary>The provider recorded on every secret a connection field owns.</summary>
    public const string Provider = "platform-connection";

    /// <summary>The descriptor name of the secret a connection field owns.</summary>
    public static string NameFor(ConnectionId connectionId, string fieldKey)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(fieldKey);
        return $"connection:{connectionId.Value}:{fieldKey}";
    }

    /// <summary>The owner of the secret that <paramref name="fieldKey"/> of <paramref name="connectionId"/> may use.</summary>
    public static SecretOwner For(ConnectionId connectionId, string fieldKey) =>
        new(Provider, NameFor(connectionId, fieldKey));
}
