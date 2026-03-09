namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Options for SQL Server persistence: retry, timeout, and optional migration behavior.
/// </summary>
public sealed record SqlPersistenceOptions
{
    /// <summary>
    /// Maximum retry count for transient SQL failures. Default 3.
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// Command timeout. Null uses provider default.
    /// </summary>
    public TimeSpan? CommandTimeout { get; init; }
}
