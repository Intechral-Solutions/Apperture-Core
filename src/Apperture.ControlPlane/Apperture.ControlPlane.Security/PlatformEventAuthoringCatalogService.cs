using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ControlPlane.Security;

public sealed class PlatformEventAuthoringCatalogService : IPlatformEventAuthoringCatalogService
{
    private static readonly IReadOnlyDictionary<string, (string DisplayName, string Description)> PlatformDisplayNames =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [PlatformEventTypes.ScheduleExpectedRunMissing] = (
                "Expected run missing",
                "Schedule expectation missed its expected run window."),
            [PlatformEventTypes.ScheduleExpectedRunCleared] = (
                "Expected run cleared",
                "Schedule expectation returned to healthy."),
            [PlatformEventTypes.ExecutionRunCompleted] = (
                "Run completed",
                "Workflow or operation run reached a successful terminal state."),
            [PlatformEventTypes.ExecutionRunFailed] = (
                "Run failed",
                "Workflow or operation run reached a failed terminal state."),
        };

    private readonly IPlatformEventCatalog _catalog;
    private readonly INotificationTemplateVariableCatalog _variables;
    private readonly IReadOnlyDictionary<string, (string DisplayName, string Description)> _moduleDisplayByType;
    private readonly NotificationDefinitionAuthorization _authorization;

    public PlatformEventAuthoringCatalogService(
        IPlatformEventCatalog catalog,
        INotificationTemplateVariableCatalog variables,
        IEnumerable<IModule> modules,
        NotificationDefinitionAuthorization authorization)
    {
        _catalog = catalog;
        _variables = variables;
        _authorization = authorization;
        _moduleDisplayByType = BuildModuleDisplayByType(modules);
    }

    public async ValueTask<IReadOnlyList<PlatformEventAuthoringCatalogEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
        return _catalog.GetAll()
            .Select(ToEntry)
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async ValueTask<PlatformEventAuthoringCatalogEntry?> GetAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(eventType) || !_catalog.IsKnownType(eventType))
            return null;

        return ToEntry(_catalog.GetRequired(eventType));
    }

    private PlatformEventAuthoringCatalogEntry ToEntry(PlatformEventTypeDescriptor descriptor)
    {
        var (displayName, description) = ResolveDisplay(descriptor.Type);
        var templateVariables = _variables.GetAllowlist(descriptor.Type)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        return new PlatformEventAuthoringCatalogEntry(
            descriptor.Type,
            descriptor.DefaultSource,
            displayName,
            description,
            descriptor.AllowedSeverities,
            descriptor.RequiresIntegrationId,
            descriptor.RequiresScheduleId,
            descriptor.RequiresRunId,
            descriptor.RequiredDataProperties,
            templateVariables);
    }

    private (string DisplayName, string? Description) ResolveDisplay(string eventType)
    {
        if (PlatformDisplayNames.TryGetValue(eventType, out var platform))
            return (platform.DisplayName, platform.Description);

        if (_moduleDisplayByType.TryGetValue(eventType, out var module))
            return (module.DisplayName, module.Description);

        return (eventType, null);
    }

    private static IReadOnlyDictionary<string, (string DisplayName, string Description)> BuildModuleDisplayByType(
        IEnumerable<IModule> modules)
    {
        var byType = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var manifest = module.GetManifest();
            if (manifest.Signals is not { Count: > 0 })
                continue;

            foreach (var signal in manifest.Signals)
            {
                if (signal is null)
                    continue;

                var fullType = ModulePlatformEventTypes.Format(manifest.Id, signal.Name);
                byType[fullType] = (signal.DisplayName.Trim(), signal.Description.Trim());
            }
        }

        return byType;
    }
}
