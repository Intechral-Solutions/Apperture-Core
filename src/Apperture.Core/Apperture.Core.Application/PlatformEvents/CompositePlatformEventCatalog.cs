using System.Linq;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Phase 2 worker catalog wrapping static Epic 77 platform-owned types plus module-declared signals.</summary>
public sealed class CompositePlatformEventCatalog : IPlatformEventCatalog
{
    private readonly PlatformEventCatalog _platform = new();

    /// <summary>Module-derived descriptors keyed by full event type (<c>apperture.module...</c>).</summary>
    private readonly IReadOnlyDictionary<string, PlatformEventTypeDescriptor> _moduleByType;

    public CompositePlatformEventCatalog(IModuleSignalRegistry moduleSignals)
    {
        ArgumentNullException.ThrowIfNull(moduleSignals);
        _moduleByType = moduleSignals.ModuleDescriptorsByType;
    }

    /// <inheritdoc />
    public bool IsKnownType(string eventType) =>
        _platform.IsKnownType(eventType) || _moduleByType.ContainsKey(eventType);

    /// <inheritdoc />
    public PlatformEventTypeDescriptor GetRequired(string eventType) =>
        _platform.IsKnownType(eventType)
            ? _platform.GetRequired(eventType)
            : _moduleByType.TryGetValue(eventType, out var d)
                ? d
                : throw new PlatformEventValidationException($"Unknown platform event type '{eventType}'.");

    /// <inheritdoc />
    public IReadOnlyList<PlatformEventTypeDescriptor> GetAll() =>
        _platform.GetAll().Concat(_moduleByType.Values).ToList();
}
