using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ControlPlane.Security;

public sealed class ConnectionVerificationService : IConnectionVerificationService
{
    private readonly IConnectorDefinitionRegistry _connectorDefinitions;
    private readonly IReadOnlyDictionary<string, IConnectionVerifier> _verifiersByConnectorId;

    public ConnectionVerificationService(
        IConnectorDefinitionRegistry connectorDefinitions,
        IEnumerable<IConnectionVerifier> verifiers)
    {
        _connectorDefinitions = connectorDefinitions;
        _verifiersByConnectorId = verifiers.ToDictionary(v => v.ConnectorDefinitionId.Value, StringComparer.Ordinal);
    }

    public async ValueTask<ConnectionVerificationResult> VerifyAsync(
        Connection connection,
        CancellationToken cancellationToken = default)
    {
        var verifiedAt = DateTimeOffset.UtcNow;
        var definition = _connectorDefinitions.Get(connection.ConnectorDefinitionId);
        if (definition is null)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Unavailable,
                verifiedAt,
                $"Connector definition '{connection.ConnectorDefinitionId.Value}' was not found.");
        }

        if (!definition.Capabilities.HasFlag(ConnectorCapabilities.SupportsConnectionVerification))
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Unavailable,
                verifiedAt,
                $"Connector '{definition.Name}' does not support test-connect.");
        }

        if (!_verifiersByConnectorId.TryGetValue(connection.ConnectorDefinitionId.Value, out var verifier))
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Unavailable,
                verifiedAt,
                $"Connector '{definition.Name}' supports test-connect metadata, but no verifier is registered.");
        }

        return await verifier.VerifyAsync(connection, cancellationToken);
    }
}
