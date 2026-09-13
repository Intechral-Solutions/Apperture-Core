using System.Diagnostics.CodeAnalysis;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Builds <see cref="IModuleSignalRegistry"/> from validated module manifests.</summary>
public sealed class ModuleSignalRegistry : IModuleSignalRegistry
{
    private static readonly IReadOnlyList<string> AllSeverities =
    [
        PlatformEventSeverities.Critical,
        PlatformEventSeverities.Error,
        PlatformEventSeverities.Warning,
        PlatformEventSeverities.Info,
    ];

    private readonly Dictionary<(string Module, string SignalNorm), ModuleSignalRegistration> _byModuleAndSignal =
        new();

    private ModuleSignalRegistry(
        IReadOnlyDictionary<string, PlatformEventTypeDescriptor> moduleDescriptorsByType,
        Dictionary<(string Module, string SignalNorm), ModuleSignalRegistration> byModuleAndSignal)
    {
        ModuleDescriptorsByType = moduleDescriptorsByType;
        _byModuleAndSignal = byModuleAndSignal;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PlatformEventTypeDescriptor> ModuleDescriptorsByType { get; }

    /// <inheritdoc />
    public bool TryResolve(string moduleId, string signalName, [NotNullWhen(true)] out ModuleSignalRegistration? registration)
    {
        registration = null;
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(signalName))
            return false;

        var signalNorm = NormalizeSignal(signalName);

        foreach (var key in AlternateModuleLookupKeys(moduleId.Trim()))
        {
            if (_byModuleAndSignal.TryGetValue((key, signalNorm), out registration))
                return true;
        }

        return false;
    }

    /// <summary>Assumes <see cref="ModuleSignalManifestValidator.ThrowIfInvalid"/> already ran or will run separately.</summary>
    public static IModuleSignalRegistry FromModules(IEnumerable<IModule> modules)
    {
        var byType = new Dictionary<string, PlatformEventTypeDescriptor>(StringComparer.Ordinal);
        var byModuleSignal = new Dictionary<(string, string), ModuleSignalRegistration>();

        foreach (var module in modules)
        {
            var manifest = module.GetManifest();
            var list = manifest.Signals;
            if (list is null || list.Count == 0)
                continue;

            var moduleId = manifest.Id.Trim();

            foreach (var declaration in list)
            {
                if (declaration is null)
                    continue;

                var signalNameNormalized = declaration.Name.Trim();
                var signalKey = NormalizeSignal(signalNameNormalized);
                var fullType = ModulePlatformEventTypes.Format(moduleId, signalNameNormalized);
                var normalizedModule = ModulePlatformEventTypes.NormalizeModuleId(moduleId);
                var source = $"/modules/{normalizedModule}";

                var descriptor = new PlatformEventTypeDescriptor(
                    fullType,
                    source,
                    AllSeverities,
                    declaration.RequiresIntegrationId,
                    RequiresScheduleId: false,
                    declaration.ResolvedRequiredDataProperties,
                    PlatformEventCatalog.DefaultMaxDataJsonLength,
                    declaration.RequiresRunId);

                if (!byType.TryAdd(descriptor.Type, descriptor))
                    throw new InvalidOperationException($"Duplicate module descriptor registration for '{descriptor.Type}'.");

                var registration = new ModuleSignalRegistration(
                    moduleId,
                    signalNameNormalized,
                    declaration,
                    descriptor);

                foreach (var modKey in AlternateModuleStorageKeys(moduleId))
                    byModuleSignal[(modKey, signalKey)] = registration;
            }
        }

        return new ModuleSignalRegistry(byType, byModuleSignal);
    }

    private static string NormalizeSignal(string signalName) =>
        signalName.Trim().ToLowerInvariant();

    /// <summary>Lookup keys tolerate manifest-declared casing for module id segments.</summary>
    private static IEnumerable<string> AlternateModuleLookupKeys(string moduleId)
    {
        yield return moduleId;
        yield return ModulePlatformEventTypes.NormalizeModuleId(moduleId);
    }

    /// <summary>Store both canonical manifest id keys and lowercase-normalized dotted id for lookups.</summary>
    private static IEnumerable<string> AlternateModuleStorageKeys(string moduleId)
    {
        var trimmed = moduleId.Trim();
        yield return trimmed;
        var normalized = ModulePlatformEventTypes.NormalizeModuleId(trimmed);
        if (!string.Equals(trimmed, normalized, StringComparison.Ordinal))
            yield return normalized;
    }
}
