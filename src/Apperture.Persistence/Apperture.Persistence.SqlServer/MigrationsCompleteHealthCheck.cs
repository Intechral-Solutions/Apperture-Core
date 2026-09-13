using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Reports unhealthy until <see cref="PersistenceMigrationHostedService"/> has applied migrations.
/// </summary>
public sealed class MigrationsCompleteHealthCheck : IHealthCheck
{
    private readonly MigrationCompletionState _state;

    public MigrationsCompleteHealthCheck(MigrationCompletionState state) => _state = state;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _state.IsComplete
                ? HealthCheckResult.Healthy("Database migrations applied.")
                : HealthCheckResult.Unhealthy("Database migrations pending."));
}
