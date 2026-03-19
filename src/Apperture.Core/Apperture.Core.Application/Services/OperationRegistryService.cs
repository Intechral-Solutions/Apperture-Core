using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// In-memory operation registry built from DI-registered modules at startup/first use.
/// </summary>
public sealed class OperationRegistryService : IOperationRegistryService
{
    private readonly IEnumerable<IModule> _modules;

    private readonly Dictionary<OperationId, OperationDefinition> _byId = new();
    private readonly Dictionary<string, List<OperationDefinition>> _byModule = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public OperationRegistryService(IEnumerable<IModule> modules)
    {
        _modules = modules ?? [];
    }

    public async ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return _byId.TryGetValue(operationId, out var def) ? def : null;
    }

    public async ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(moduleId))
            return Array.Empty<OperationDefinition>();

        return _byModule.TryGetValue(moduleId, out var list) ? list : Array.Empty<OperationDefinition>();
    }

    public async ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        _byId[definition.Id] = definition;

        var moduleId = definition.ModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
            return;

        if (!_byModule.TryGetValue(moduleId, out var list))
        {
            list = new List<OperationDefinition>();
            _byModule[moduleId] = list;
        }

        // Replace/insert by operation id for stability.
        list.RemoveAll(d => Equals(d.Id, definition.Id));
        list.Add(definition);
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            foreach (var module in _modules)
            {
                if (module is not IOperationDefinitionProvider provider)
                    continue;

                await foreach (var def in provider.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
                {
                    _byId[def.Id] = def;

                    var moduleId = def.ModuleId;
                    if (string.IsNullOrWhiteSpace(moduleId))
                        continue;

                    if (!_byModule.TryGetValue(moduleId, out var list))
                    {
                        list = new List<OperationDefinition>();
                        _byModule[moduleId] = list;
                    }

                    if (list.All(x => !Equals(x.Id, def.Id)))
                        list.Add(def);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }
}

