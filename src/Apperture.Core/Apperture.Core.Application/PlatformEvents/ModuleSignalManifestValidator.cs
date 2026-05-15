using System.Text.RegularExpressions;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Static Epic 77 Phase 2 validation for manifest-declared module signals.</summary>
public static class ModuleSignalManifestValidator
{
    private static readonly Regex NamePattern =
        new(@"^[a-z0-9]+(\.[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CamelPropPattern =
        new(@"^[a-z][a-zA-Z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedSeverities = new(StringComparer.Ordinal)
    {
        PlatformEventSeverities.Critical,
        PlatformEventSeverities.Error,
        PlatformEventSeverities.Warning,
        PlatformEventSeverities.Info,
    };

    /// <summary>Throws <see cref="InvalidOperationException"/> when any module manifests invalid signals.</summary>
    public static void ThrowIfInvalid(IEnumerable<IModule> modules)
    {
        var moduleList = modules as IReadOnlyList<IModule> ?? modules.ToList();
        var msgs = new List<string>();
        var globalFullTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in moduleList)
        {
            msgs.AddRange(EnumerateSingleModuleIssues(module));
            AccumulateDuplicateFullTypes(module, globalFullTypes, msgs);
        }

        if (msgs.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, msgs));
    }

    private static void AccumulateDuplicateFullTypes(
        IModule module,
        HashSet<string> globalFullTypes,
        ICollection<string> msgs)
    {
        var manifest = module.GetManifest();
        var signals = manifest.Signals;
        if (signals is null || signals.Count == 0)
            return;

        foreach (var declaration in signals)
        {
            if (declaration is null)
                continue;

            var full = ModulePlatformEventTypes.Format(manifest.Id, declaration.Name);
            if (!globalFullTypes.Add(full))
                msgs.Add($"Duplicate full platform signal type '{full}' across WorkerHost manifests.");
        }
    }

    private static IEnumerable<string> EnumerateSingleModuleIssues(IModule module)
    {
        var manifest = module.GetManifest();
        var signals = manifest.Signals;
        if (signals is null || signals.Count == 0)
            yield break;

        var moduleId = manifest.Id.Trim();
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            yield return "Manifest has signals but missing module Id.";
            yield break;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in signals)
        {
            if (declaration is null)
            {
                yield return $"Module '{moduleId}': null signal declaration entry.";
                continue;
            }

            var nameTrim = declaration.Name.Trim();
            if (!seenNames.Add(nameTrim))
                yield return $"Module '{moduleId}': duplicate signal name '{nameTrim}'.";

            if (string.IsNullOrWhiteSpace(declaration.DisplayName.Trim()))
                yield return $"Module '{moduleId}': signal '{nameTrim}' DisplayName must be non-empty.";

            if (string.IsNullOrWhiteSpace(declaration.Description.Trim()))
                yield return $"Module '{moduleId}': signal '{nameTrim}' Description must be non-empty.";

            if (!NamePattern.IsMatch(nameTrim))
                yield return $"Module '{moduleId}': signal '{nameTrim}' Name must match pattern [a-z0-9]+(.[a-z0-9]+)*.";

            if (string.IsNullOrWhiteSpace(declaration.DefaultSeverity)
                || !AllowedSeverities.Contains(declaration.DefaultSeverity))
            {
                yield return $"Module '{moduleId}': signal '{nameTrim}' DefaultSeverity must be critical|error|warning|info.";
            }

            foreach (var p in EnumerateRequiredDataPropertyIssues(moduleId, nameTrim, declaration))
                yield return p;
        }
    }

    private static IEnumerable<string> EnumerateRequiredDataPropertyIssues(
        string moduleId,
        string signalName,
        ModuleSignalDeclaration declaration)
    {
        var props = declaration.RequiredDataProperties;
        if (props is null || props.Count == 0)
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in props)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                yield return $"Module '{moduleId}': signal '{signalName}' RequiredDataProperties must not contain empty names.";
                continue;
            }

            var p = raw.Trim();
            if (!seen.Add(p))
                yield return $"Module '{moduleId}': signal '{signalName}' duplicates required data property '{p}'.";

            if (!CamelPropPattern.IsMatch(p))
                yield return $"Module '{moduleId}': signal '{signalName}' required data property '{p}' must be camelCase ([a-z] then alphanumerics).";
        }
    }
}
