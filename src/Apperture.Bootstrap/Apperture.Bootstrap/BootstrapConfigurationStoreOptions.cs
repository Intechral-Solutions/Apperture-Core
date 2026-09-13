namespace Apperture.Bootstrap;

/// <summary>
/// Options for the file-backed bootstrap configuration store.
/// </summary>
public sealed class BootstrapConfigurationStoreOptions
{
    /// <summary>Environment variable that overrides the bootstrap configuration file path.</summary>
    public const string ConfigPathEnvironmentVariableName = "APPERTURE_BOOTSTRAP_CONFIG_PATH";

    /// <summary>
    /// Full path to <c>configuration.json</c>. When null, resolved via
    /// <see cref="BootstrapConfigurationStorePathResolver"/> at runtime.
    /// </summary>
    public string? ConfigurationFilePath { get; set; }
}
