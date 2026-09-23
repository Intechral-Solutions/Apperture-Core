using Apperture.Core.Application.ComponentCatalog;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Aggregates the per-family <see cref="IComponentCatalogProvider"/>s into one authorized, read-only
/// catalog. Modelled on <see cref="InventoryQueryService"/>: collect, derive facets from what was
/// actually collected, filter in memory, sort, bound.
/// </summary>
/// <remarks>
/// The list path deliberately never touches module metadata. Resolving a module's display name goes
/// through <see cref="IModuleWorkflowListingService"/>, which builds every module descriptor (twice) per
/// call; that cost is paid on detail only, once, and only for callers holding <c>module.configure</c>.
/// </remarks>
public sealed class ComponentCatalogService : IComponentCatalog
{
    private readonly IReadOnlyList<IComponentCatalogProvider> _providers;
    private readonly IModuleWorkflowListingService _modules;

    public ComponentCatalogService(
        IEnumerable<IComponentCatalogProvider> providers,
        IModuleWorkflowListingService modules)
    {
        _providers = providers.ToList();
        _modules = modules;
    }

    public async ValueTask<ComponentCatalogResult> QueryAsync(
        ComponentCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        var wanted = _providers.Where(p => Wants(query.Kind, p.Kind)).ToList();

        var collected = new List<ComponentSummary>();
        var denied = 0;
        foreach (var provider in wanted)
        {
            try
            {
                collected.AddRange(await provider.ListAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException)
            {
                // Omit the whole family and record nothing about it.
                denied++;
            }
        }

        if (wanted.Count > 0 && denied == wanted.Count)
            throw new UnauthorizedAccessException("Not authorized to view any component family.");

        // Facets come from what was actually collected, never from the static token set.
        var availableKinds = collected
            .Select(c => c.Key.Kind)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var availableModules = collected
            .Where(c => c.SupplyingModuleId is not null)
            .Select(c => c.SupplyingModuleId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m, StringComparer.Ordinal)
            .Select(m => new ComponentModuleRef(m))
            .ToList();

        var matching = collected
            .Where(c => Matches(c, query))
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Key.Kind, StringComparer.Ordinal)
            .ThenBy(c => c.Key.NativeId, StringComparer.Ordinal)
            .ToList();

        // Same convention as Inventory: a non-positive Take means "use the default"; anything larger is capped.
        var take = query.Take <= 0 ? ComponentCatalogQuery.DefaultTake : Math.Min(query.Take, ComponentCatalogQuery.MaxTake);
        return new ComponentCatalogResult(matching.Take(take).ToList(), availableKinds, availableModules, matching.Count);
    }

    public async ValueTask<ComponentDetail?> GetAsync(ComponentKey key, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Kind, key.Kind, StringComparison.Ordinal));
        if (provider is null)
            return null;

        ComponentDetail? detail;
        try
        {
            detail = await provider.GetAsync(key.NativeId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            // 404, never 403: a denial would confirm the Component exists.
            return null;
        }

        if (detail?.Summary.SupplyingModuleId is not { } moduleId)
            return detail;

        return detail with { SupplyingModule = await ResolveModuleAsync(moduleId, cancellationToken).ConfigureAwait(false) };
    }

    private async ValueTask<ComponentModuleAttribution?> ResolveModuleAsync(string moduleId, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _modules.GetModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
            return entry is null
                ? null
                : new ComponentModuleAttribution(
                    entry.Descriptor.ModuleId,
                    entry.Descriptor.DisplayName,
                    entry.Descriptor.Version,
                    entry.State.ToString());
        }
        catch (UnauthorizedAccessException)
        {
            // Module metadata is gated by module.configure; the bare id on the summary remains.
            return null;
        }
    }

    private static bool Wants(string? requestedKind, string providerKind) =>
        string.IsNullOrWhiteSpace(requestedKind) || string.Equals(requestedKind, providerKind, StringComparison.Ordinal);

    private static bool Matches(ComponentSummary item, ComponentCatalogQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Origin)
            && !string.Equals(item.Origin, query.Origin, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(query.ModuleId)
            && !string.Equals(item.SupplyingModuleId, query.ModuleId, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(query.Q))
            return true;

        var q = query.Q.Trim();
        return item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || item.Key.NativeId.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (item.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
