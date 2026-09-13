using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

public sealed record ModuleLifecycleBlocker(string Code, string Message);

public enum ModuleLifecycleEffect
{
    Immediate = 0,
    PartialUntilRestart = 1,
    RequiresRestart = 2
}

public sealed record ModuleLifecycleTransitionResult(
    bool Succeeded,
    string ModuleId,
    string Action,
    ModuleLifecycleState State,
    ModuleLifecycleEffect Effect,
    IReadOnlyList<ModuleLifecycleBlocker> Blockers);

public sealed record ModuleCatalogEntry(
    ModuleDescriptor Descriptor,
    ModuleLifecycleState State,
    bool Installed,
    bool Enabled,
    bool EffectivelyActive,
    IReadOnlyList<ModuleLifecycleBlocker> ValidationIssues,
    /// <summary>
    /// Persisted installation-state last-changed time (<c>ModuleInstallationStates.UpdatedAt</c>).
    /// Null for modules that are discovered but never installed — there is no persisted
    /// timestamp to report for those, and none is fabricated.
    /// </summary>
    DateTimeOffset? UpdatedAtUtc = null);
