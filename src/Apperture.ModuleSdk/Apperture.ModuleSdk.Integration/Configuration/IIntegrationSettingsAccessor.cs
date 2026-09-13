namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Runtime access to resolved integration-instance settings for the current execution context.
/// Implementations live in platform/runtime adapters — not in module projects.
/// </summary>
public interface IIntegrationSettingsAccessor
{
    /// <summary>Gets a converted value or throws <see cref="IntegrationConfigurationException"/> when missing/invalid.</summary>
    T GetRequired<T>(string key);

    /// <summary>Gets a converted value, or <paramref name="defaultValue"/> when unset.</summary>
    T GetOrDefault<T>(string key, T defaultValue);

    /// <summary>True when a non-sensitive value is present or a sensitive value has secret presence.</summary>
    bool IsConfigured(string key);

    /// <summary>True when a sensitive setting has a resolvable secret reference (never implies plaintext in APIs).</summary>
    bool IsSecretConfigured(string key);

    /// <summary>
    /// Resolves sensitive setting plaintext for authorized module code.
    /// Must not be logged by callers; platform adapters redact in diagnostics.
    /// </summary>
    ValueTask<string> GetSecretAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Preferred typed options binding over raw key access.
/// Platform adapters bind declared settings onto <typeparamref name="TOptions"/>.
/// </summary>
public interface IIntegrationOptions<TOptions>
    where TOptions : class
{
    TOptions Value { get; }
}
