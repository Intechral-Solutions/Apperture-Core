using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Evaluates connection eligibility for slot binding. Filters by active state and connector/contract compatibility.
/// </summary>
public sealed class BindingEligibilityService : IBindingEligibilityService
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IConnectorDefinitionRegistry _connectorRegistry;

    public BindingEligibilityService(
        IConnectionRepository connectionRepository,
        IConnectorDefinitionRegistry connectorRegistry)
    {
        _connectionRepository = connectionRepository;
        _connectorRegistry = connectorRegistry;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        CancellationToken cancellationToken = default)
    {
        var active = await _connectionRepository.GetActiveAsync(cancellationToken);

        // Filter by contract type: connector definition must exist; permissive for now (no SupportedContractTypes)
        var eligible = new List<Connection>();
        foreach (var conn in active)
        {
            var def = _connectorRegistry.Get(conn.ConnectorDefinitionId);
            if (def is null)
                continue;
            // Future: check def.SupportedContractTypes contains contractType
            eligible.Add(conn);
        }

        return eligible;
    }
}
