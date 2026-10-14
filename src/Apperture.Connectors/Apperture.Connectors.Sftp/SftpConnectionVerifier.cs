using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Renci.SshNet.Common;

namespace Apperture.Connectors.Sftp;

public sealed class SftpConnectionVerifier : IConnectionVerifier
{
    private readonly IConnectionMaterialResolver _materialResolver;
    private readonly SftpConnectorClientFactory _factory;

    public SftpConnectionVerifier(IConnectionMaterialResolver materialResolver, SftpConnectorClientFactory factory)
    {
        _materialResolver = materialResolver;
        _factory = factory;
    }

    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformSftp;

    public async ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        ResolvedConnectionMaterial material;
        try
        {
            material = await _materialResolver.ResolveAsync(connection, ConnectionResolutionPath.Verify, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                ConnectionVerificationMessages.SecretResolutionFailed);
        }

        try
        {
            using var client = _factory.Create(connection, material);
            return new ConnectionVerificationResult(
                client.IsConnected ? ConnectionVerificationStatus.Success : ConnectionVerificationStatus.Failed,
                at,
                client.IsConnected ? "Connected successfully." : "Connection attempt completed but client did not report connected state.");
        }
        catch (SshAuthenticationException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                "Authentication failed while connecting to the SFTP server.");
        }
        catch (SshConnectionException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SFTP connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"SFTP verification failed: {ex.Message}");
        }
    }
}
