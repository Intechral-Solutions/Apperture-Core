using Apperture.Bootstrap;
using Microsoft.Data.SqlClient;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Test-only bootstrap profile so the Management Host can run under ASPNETCORE_ENVIRONMENT=Testing
/// (BootstrapConnectionProfile rejects config-based SQL outside Development).
/// </summary>
internal sealed class FixedBootstrapConnectionProfile : IBootstrapConnectionProfile
{
    public FixedBootstrapConnectionProfile(string connectionString) =>
        ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public string ConnectionString { get; }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}
