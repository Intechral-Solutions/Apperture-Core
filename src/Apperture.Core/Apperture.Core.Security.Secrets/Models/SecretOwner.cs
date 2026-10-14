namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// The provider and name a secret's stored descriptor must have for a holder (a connection field, say) to use it. A reference
/// is only an identifier: it names a secret, and says nothing about who owns it. The owner is derived from the holder, never
/// from the reference, and is compared with the secret's own stored descriptor.
/// </summary>
/// <remarks>
/// Both parts are compared exactly (ordinal, case sensitive) wherever an owner is checked. A holder kind defines its own rule
/// in one place so that the rule that stores a secret and the rule that reads it cannot drift apart (for a connection field,
/// <c>ConnectionSecretOwnership</c>).
/// </remarks>
public sealed record SecretOwner
{
    public SecretOwner(string provider, string name)
    {
        if (string.IsNullOrEmpty(provider))
            throw new ArgumentException("A secret owner has a provider.", nameof(provider));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("A secret owner has a name.", nameof(name));
        Provider = provider;
        Name = name;
    }

    /// <summary>The provider recorded on the secret's descriptor.</summary>
    public string Provider { get; }

    /// <summary>The name recorded on the secret's descriptor.</summary>
    public string Name { get; }

    /// <summary>True only when both parts are exactly equal (ordinal).</summary>
    public bool Matches(string? provider, string? name) =>
        string.Equals(Provider, provider, StringComparison.Ordinal) && string.Equals(Name, name, StringComparison.Ordinal);

    /// <summary>Redacted: an owner name embeds the holder's identifiers, which a log line has no use for.</summary>
    public override string ToString() => nameof(SecretOwner);
}
