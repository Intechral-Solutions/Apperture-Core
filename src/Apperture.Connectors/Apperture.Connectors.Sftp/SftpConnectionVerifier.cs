using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Renci.SshNet.Common;

namespace Apperture.Connectors.Sftp;

public sealed class SftpConnectionVerifier : IConnectionVerifier
{
    private readonly SftpConnectorClientFactory _factory;

    public SftpConnectionVerifier(SftpConnectorClientFactory factory) => _factory = factory;

    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformSftp;

    public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        try
        {
            var material = new Core.Application.Connector.ResolvedConnectionMaterial(
                connection.Config.Concat(connection.SecretRefs).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
            using var client = _factory.Create(connection, material);
            return ValueTask.FromResult(new ConnectionVerificationResult(
                client.IsConnected ? ConnectionVerificationStatus.Success : ConnectionVerificationStatus.Failed,
                at,
                client.IsConnected ? "Connected successfully." : "Connection attempt completed but client did not report connected state."));
        }
        catch (SshAuthenticationException)
        {
            return ValueTask.FromResult(new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                "Authentication failed while connecting to the SFTP server."));
        }
        catch (SshConnectionException ex)
        {
            return ValueTask.FromResult(new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SFTP connection failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SFTP verification failed: {ex.Message}"));
        }
    }
}
