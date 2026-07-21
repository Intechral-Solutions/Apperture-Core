using System.Linq;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates connection eligibility for slot binding. Filters by active state and connector/contract compatibility.
/// </summary>
public sealed class BindingEligibilityService : IBindingEligibilityService
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IConnectorDefinitionRegistry _connectorRegistry;
    private readonly IConnectorClientFactoryRegistry? _factoryRegistry;

    public BindingEligibilityService(
        IConnectionRepository connectionRepository,
        IConnectorDefinitionRegistry connectorRegistry,
        IConnectorClientFactoryRegistry? factoryRegistry = null)
    {
        _connectionRepository = connectionRepository;
        _connectorRegistry = connectorRegistry;
        _factoryRegistry = factoryRegistry;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        CancellationToken cancellationToken = default)
    {
        var active = await _connectionRepository.GetActiveAsync(cancellationToken);

        var contract = ResolveContractType(contractType);
        var eligible = new List<Connection>();
        foreach (var conn in active)
        {
            var def = _connectorRegistry.Get(conn.ConnectorDefinitionId);
            if (def is null)
                continue;

            if (!IsCompatible(conn.ConnectorDefinitionId, contract))
                continue;

            eligible.Add(conn);
        }

        return eligible;
    }

    private bool IsCompatible(ConnectorDefinitionId connectorDefinitionId, Type? contractType)
    {
        if (_factoryRegistry is null || contractType is null)
            return true;

        var getFactory = typeof(IConnectorClientFactoryRegistry)
            .GetMethods()
            .FirstOrDefault(m => string.Equals(m.Name, nameof(IConnectorClientFactoryRegistry.GetFactory), StringComparison.Ordinal)
                                 && m.IsGenericMethod
                                 && m.GetParameters().Length == 1);
        if (getFactory is null)
            return true;

        var generic = getFactory.MakeGenericMethod(contractType);
        var factory = generic.Invoke(_factoryRegistry, [connectorDefinitionId]);
        return factory is not null;
    }

    private static Type? ResolveContractType(string contractType)
    {
        if (string.IsNullOrWhiteSpace(contractType))
            return null;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var resolved = asm.GetType(contractType, throwOnError: false, ignoreCase: false);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }
}
