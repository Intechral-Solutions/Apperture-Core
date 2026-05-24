namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Tracks whether startup EF migrations have completed (used by readiness health checks).
/// </summary>
public sealed class MigrationCompletionState
{
    public bool IsComplete { get; set; }
}
