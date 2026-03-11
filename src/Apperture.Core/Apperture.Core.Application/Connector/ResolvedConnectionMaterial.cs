namespace Apperture.Core.Application.Connector;

/// <summary>
/// Immutable material for building a connector client: merged config and resolved secret values.
/// Passed only to connector factories; never log or expose raw values.
/// </summary>
public sealed record ResolvedConnectionMaterial
{
    /// <summary>
    /// Merged configuration and resolved secrets. Keys from Connection.Config and Connection.SecretRefs;
    /// values from config or ISecretResolver. Do not log or serialize.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfigAndResolvedSecrets { get; }

    public ResolvedConnectionMaterial(IReadOnlyDictionary<string, string> configAndResolvedSecrets)
    {
        ConfigAndResolvedSecrets = configAndResolvedSecrets ?? throw new ArgumentNullException(nameof(configAndResolvedSecrets));
    }

    /// <summary>
    /// Redacted; does not include values to prevent accidental logging of secret material.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ResolvedConnectionMaterial)}(KeyCount={ConfigAndResolvedSecrets.Count})";
}
