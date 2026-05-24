namespace Apperture.Bootstrap;

/// <summary>
/// Resolves the bootstrap configuration file path from options or environment.
/// </summary>
public static class BootstrapConfigurationStorePathResolver
{
    /// <summary>
    /// Default: <c>%ProgramData%\Apperture\bootstrap\configuration.json</c> on Windows;
    /// <c>/etc/apperture/bootstrap/configuration.json</c> on Linux; ApplicationData fallback elsewhere.
    /// </summary>
    public static string GetDefaultConfigurationFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "Apperture", "bootstrap", "configuration.json");
        }

        if (OperatingSystem.IsLinux())
        {
            return "/etc/apperture/bootstrap/configuration.json";
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Apperture", "bootstrap", "configuration.json");
    }

    /// <summary>
    /// Resolves path: <see cref="BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName"/>,
    /// then <paramref name="options"/>.<see cref="BootstrapConfigurationStoreOptions.ConfigurationFilePath"/>,
    /// then <see cref="GetDefaultConfigurationFilePath"/>.
    /// </summary>
    public static string Resolve(BootstrapConfigurationStoreOptions? options = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options?.ConfigurationFilePath))
        {
            return Path.GetFullPath(options.ConfigurationFilePath.Trim());
        }

        return GetDefaultConfigurationFilePath();
    }
}
