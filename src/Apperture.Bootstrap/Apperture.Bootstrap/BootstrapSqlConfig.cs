namespace Apperture.Bootstrap;

/// <summary>
/// Bootstrap SQL Server configuration. Credentials come from config or environment, not from operational secret store.
/// </summary>
public sealed class BootstrapSqlConfig
{
    public const string SectionName = "SqlServer";

    /// <summary>
    /// SQL Server host/instance (e.g. localhost, (localdb)\mssqllocaldb).
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Database name. Defaults to Apperture if not set.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// SQL authentication user. When set with Password, uses SQL auth instead of Integrated Security.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// SQL authentication password. When set with User, uses SQL auth.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// When true, skips server certificate validation (dev only).
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;
}
