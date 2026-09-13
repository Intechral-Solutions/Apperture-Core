using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// In-memory procedure registry built from module manifests. Rebuilds when the enabled-module set changes
/// (Epic 56B) so catalog state cannot stay empty after module installs become effective.
/// </summary>
public sealed class ProcedureRegistryService : IProcedureRegistryService
{
    private readonly IEnumerable<IModule> _modules;
    private readonly IModuleActivationFilter? _activationFilter;

    private readonly Dictionary<string, ProcedureDefinition> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ProcedureDefinition>> _byModule = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string? _lastActivationSignature;
    private bool _hasCatalog;

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

    public async ValueTask<ProcedureDefinition?> FindByTargetAsync(
        string moduleId,
        ProcedureLaunchKind launchKind,
        string targetDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!_byModule.TryGetValue(moduleId, out var list))
            return null;

        return list.FirstOrDefault(p =>
            p.LaunchKind == launchKind
            && string.Equals(p.TargetDefinitionId, targetDefinitionId, StringComparison.Ordinal));
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var signature = await ComputeActivationSignatureAsync(cancellationToken).ConfigureAwait(false);
        if (_hasCatalog && string.Equals(signature, _lastActivationSignature, StringComparison.Ordinal))
            return;

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            signature = await ComputeActivationSignatureAsync(cancellationToken).ConfigureAwait(false);
            if (_hasCatalog && string.Equals(signature, _lastActivationSignature, StringComparison.Ordinal))
                return;

            _byId.Clear();
            _byModule.Clear();

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

            _lastActivationSignature = signature;
            _hasCatalog = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Stable signature of which modules currently contribute procedures (order-independent).
    /// </summary>
    private async ValueTask<string> ComputeActivationSignatureAsync(CancellationToken cancellationToken)
    {
        if (_activationFilter is null)
        {
            var allIds = _modules
                .Select(m => m.GetManifest().Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            return string.Join('|', allIds);
        }

        var enabled = new List<string>();
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            if (string.IsNullOrWhiteSpace(manifest.Id))
                continue;
            if (await _activationFilter.IsEnabledAsync(manifest.Id, cancellationToken).ConfigureAwait(false))
                enabled.Add(manifest.Id);
        }

        enabled.Sort(StringComparer.Ordinal);
        return string.Join('|', enabled);
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
