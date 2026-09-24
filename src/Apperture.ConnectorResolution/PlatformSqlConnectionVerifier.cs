using System.Data;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Data.SqlClient;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Verifies platform SQL connections by opening <see cref="SqlConnection"/> from resolved material and executing <c>SELECT 1</c>.
/// </summary>
public sealed class PlatformSqlConnectionVerifier : IConnectionVerifier
{
    private readonly ISecretResolver _secretResolver;
    private readonly IConnectorClientFactory<IDbConnection> _factory;

    public PlatformSqlConnectionVerifier(
        ISecretResolver secretResolver,
        IConnectorClientFactory<IDbConnection> factory)
    {
        _secretResolver = secretResolver;
        _factory = factory;
    }

    /// <inheritdoc />
    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformSql;

    /// <inheritdoc />
    public async ValueTask<ConnectionVerificationResult> VerifyAsync(
        Connection connection,
        CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        ResolvedConnectionMaterial material;
        try
        {
            material = await ResolvedConnectionMaterialBuilder
                .BuildAsync(connection, _secretResolver, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                ConnectionVerificationMessages.SecretResolutionFailed);
        }

        var raw = _factory.Create(connection, material);
        if (raw is not SqlConnection conn)
        {
            raw.Dispose();
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                "Platform SQL factory must return a SqlConnection.");
        }

        try
        {
            await using (conn)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = 15;
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is null or DBNull)
                {
                    return new ConnectionVerificationResult(
                        ConnectionVerificationStatus.Failed,
                        at,
                        "SQL probe returned no value.");
                }

                return new ConnectionVerificationResult(
                    ConnectionVerificationStatus.Success,
                    at,
                    "Connected to SQL Server and executed probe query (SELECT 1).");
            }
        }
        catch (SqlException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SQL verification failed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"Invalid SQL connection configuration: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SQL connection timed out: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SQL verification failed: {ex.Message}");
        }
    }
}
