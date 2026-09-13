namespace Apperture.Bootstrap;

/// <summary>
/// Thrown when bootstrap configuration file I/O or JSON parsing fails.
/// </summary>
public sealed class BootstrapConfigurationPersistenceException : Exception
{
    public BootstrapConfigurationPersistenceException(string message)
        : base(message)
    {
    }

    public BootstrapConfigurationPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? FilePath { get; init; }
}
