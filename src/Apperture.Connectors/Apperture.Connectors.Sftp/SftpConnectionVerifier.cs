using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Renci.SshNet.Common;

namespace Apperture.Connectors.Sftp;

public sealed class SftpConnectionVerifier : IConnectionVerifier
{
    private readonly ISecretResolver _secretResolver;
    private readonly SftpConnectorClientFactory _factory;

    public SftpConnectionVerifier(ISecretResolver secretResolver, SftpConnectorClientFactory factory)
    {
        _secretResolver = secretResolver;
        _factory = factory;
    }

    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformSftp;

    public async ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        ResolvedConnectionMaterial material;
        try
        {
            material = await BuildMaterialAsync(connection, _secretResolver, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"Could not resolve connection secrets: {ex.Message}");
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

    private static async ValueTask<ResolvedConnectionMaterial> BuildMaterialAsync(
        Connection connection,
        ISecretResolver secretResolver,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(connection.Config);
        foreach (var kv in connection.SecretRefs)
        {
            var secretRef = new SecretRef(kv.Value);
            var resolved = await secretResolver.ResolveAsync(secretRef, cancellationToken).ConfigureAwait(false);
            var value = resolved.Material.OpaqueString ?? resolved.Material.Json ?? "";
            merged[kv.Key] = value;
        }

        return new ResolvedConnectionMaterial(merged);
    }
}
