using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// In-memory procedure registry built from module manifests at first use (lifecycle-governed like operations).
/// </summary>
public sealed class ProcedureRegistryService : IProcedureRegistryService
{
    private readonly IEnumerable<IModule> _modules;
    private readonly IModuleActivationFilter? _activationFilter;

    private readonly Dictionary<string, ProcedureDefinition> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ProcedureDefinition>> _byModule = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public ProcedureRegistryService(IEnumerable<IModule> modules, IModuleActivationFilter? activationFilter = null)
    {
        _modules = modules ?? [];
        _activationFilter = activationFilter;
    }

    public async ValueTask<ProcedureDefinition?> GetProcedureAsync(string procedureId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return _byId.TryGetValue(procedureId, out var def) ? def : null;
    }

    public async ValueTask<IReadOnlyList<ProcedureDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return _byId.Values
            .OrderBy(d => d.ModuleId, StringComparer.Ordinal)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask<IReadOnlyList<ProcedureDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(moduleId))
            return Array.Empty<ProcedureDefinition>();

        return _byModule.TryGetValue(moduleId, out var list) ? list : Array.Empty<ProcedureDefinition>();
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
                var manifest = module.GetManifest();
                if (_activationFilter is not null && !await _activationFilter.IsEnabledAsync(manifest.Id, cancellationToken).ConfigureAwait(false))
                    continue;

                var procedures = manifest.Procedures;
                if (procedures is null || procedures.Count == 0)
                    continue;

                foreach (var p in procedures)
                {
                    var def = new ProcedureDefinition(
                        p.Id,
                        manifest.Id,
                        p.DisplayName,
                        p.Description,
                        p.Category,
                        p.LaunchKind,
                        p.TargetDefinitionId,
                        p.ParameterSchema,
                        p.RequiredPermissions);

                    Validate(def);
                    if (_byId.TryGetValue(def.Id, out var existing) && !Equals(existing, def))
                        throw new InvalidOperationException($"Duplicate procedure id '{def.Id}' with conflicting metadata.");

                    _byId[def.Id] = def;

                    if (!_byModule.TryGetValue(manifest.Id, out var list))
                    {
                        list = new List<ProcedureDefinition>();
                        _byModule[manifest.Id] = list;
                    }

                    list.RemoveAll(x => string.Equals(x.Id, def.Id, StringComparison.Ordinal));
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

    private static void Validate(ProcedureDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new InvalidOperationException("Procedure id is required.");
        if (string.IsNullOrWhiteSpace(definition.ModuleId))
            throw new InvalidOperationException($"Procedure '{definition.Id}' must belong to a module.");
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            throw new InvalidOperationException($"Procedure '{definition.Id}' must declare DisplayName.");
        if (string.IsNullOrWhiteSpace(definition.TargetDefinitionId))
            throw new InvalidOperationException($"Procedure '{definition.Id}' must declare TargetDefinitionId.");
    }
}
