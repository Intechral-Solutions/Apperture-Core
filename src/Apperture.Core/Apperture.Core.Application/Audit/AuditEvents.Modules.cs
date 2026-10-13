using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 7: the typed producers of the module lifecycle. The module is the target and the related module reference. A row
// records the persisted installation state before and after, the persisted version, and the restart effect the transition
// reports; it never claims that a module is active in any runtime process. A blocked attempt names the blocker codes and never
// their messages (a message is free text and may name other modules' settings).
public static partial class AuditEvents
{
    private static AuditRelatedReferences ModuleRelated(string moduleId) =>
        new(ModuleId: BoundedId(moduleId, AuditLogModuleIdLength));

    private static string ModuleStateValue(ModuleLifecycleState state) => state switch
    {
        ModuleLifecycleState.Discovered => "discovered",
        ModuleLifecycleState.InstalledDisabled => "installed_disabled",
        ModuleLifecycleState.InstalledEnabled => "installed_enabled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a module lifecycle state."),
    };

    private static string ModuleEffectValue(ModuleLifecycleEffect effect) => effect switch
    {
        ModuleLifecycleEffect.Immediate => "immediate",
        ModuleLifecycleEffect.PartialUntilRestart => "partial_until_restart",
        ModuleLifecycleEffect.RequiresRestart => "requires_restart",
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, "Not a module lifecycle effect."),
    };

    /// <summary>
    /// A module was installed. <paramref name="implicitInstall"/> marks the installation an enable request performed for a module
    /// that was not installed; it is the secondary row beside the enable row.
    /// </summary>
    public static AuditEvent ModuleInstalled(
        string moduleId, string version, ModuleLifecycleState previousState, ModuleLifecycleState state, ModuleLifecycleEffect effect, bool implicitInstall = false) =>
        ModuleChanged(AuditActionNames.ModuleInstalled, moduleId, version, null, previousState, state, effect, implicitInstall);

    /// <summary>
    /// A module's installation was enabled. <paramref name="previousVersion"/> is set when the enable also moved the installation to
    /// the discovered version. <paramref name="implicitInstall"/> marks an enable that installed the module in the same transition
    /// (the primary row carries the flag beside its secondary install row).
    /// </summary>
    public static AuditEvent ModuleEnabled(
        string moduleId, string version, string? previousVersion, ModuleLifecycleState previousState, ModuleLifecycleState state, ModuleLifecycleEffect effect, bool implicitInstall = false) =>
        ModuleChanged(AuditActionNames.ModuleEnabled, moduleId, version, previousVersion, previousState, state, effect, implicitInstall);

    /// <summary>A module's installation was disabled.</summary>
    public static AuditEvent ModuleDisabled(
        string moduleId, string version, ModuleLifecycleState previousState, ModuleLifecycleState state, ModuleLifecycleEffect effect) =>
        ModuleChanged(AuditActionNames.ModuleDisabled, moduleId, version, null, previousState, state, effect, implicitInstall: false);

    /// <summary>A module's installation was removed. <paramref name="version"/> is the version that was installed.</summary>
    public static AuditEvent ModuleUninstalled(
        string moduleId, string version, ModuleLifecycleState previousState, ModuleLifecycleState state, ModuleLifecycleEffect effect) =>
        ModuleChanged(AuditActionNames.ModuleUninstalled, moduleId, version, null, previousState, state, effect, implicitInstall: false);

    private static AuditEvent ModuleChanged(
        string action,
        string moduleId,
        string version,
        string? previousVersion,
        ModuleLifecycleState previousState,
        ModuleLifecycleState state,
        ModuleLifecycleEffect effect,
        bool implicitInstall) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.Module, moduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("version", version),
                ("previousVersion", BoundedId(previousVersion)),
                ("previousState", ModuleStateValue(previousState)),
                ("state", ModuleStateValue(state)),
                ("restartEffect", ModuleEffectValue(effect)),
                ("implicitInstall", implicitInstall ? true : null)),
            ModuleRelated(moduleId));

    /// <summary>An install request was refused. <paramref name="state"/> is the module's persisted state when it was refused.</summary>
    public static AuditEvent ModuleInstallBlocked(string moduleId, ModuleLifecycleState state, IReadOnlyList<string> blockerCodes) =>
        ModuleBlocked(AuditActionNames.ModuleInstallBlocked, moduleId, state, blockerCodes);

    /// <summary>An enable request was refused.</summary>
    public static AuditEvent ModuleEnableBlocked(string moduleId, ModuleLifecycleState state, IReadOnlyList<string> blockerCodes) =>
        ModuleBlocked(AuditActionNames.ModuleEnableBlocked, moduleId, state, blockerCodes);

    /// <summary>A disable request was refused.</summary>
    public static AuditEvent ModuleDisableBlocked(string moduleId, ModuleLifecycleState state, IReadOnlyList<string> blockerCodes) =>
        ModuleBlocked(AuditActionNames.ModuleDisableBlocked, moduleId, state, blockerCodes);

    /// <summary>An uninstall request was refused.</summary>
    public static AuditEvent ModuleUninstallBlocked(string moduleId, ModuleLifecycleState state, IReadOnlyList<string> blockerCodes) =>
        ModuleBlocked(AuditActionNames.ModuleUninstallBlocked, moduleId, state, blockerCodes);

    /// <summary>The blocker codes of a refusal: bounded identifiers, each once, in ordinal order, at most the schema's bound.</summary>
    private static List<string> BlockerCodes(IReadOnlyList<string> blockerCodes)
    {
        var codes = blockerCodes
            .Select(c => BoundedId(c))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .Take(16)
            .ToList();
        if (codes.Count == 0)
            throw new ArgumentException("A refused attempt names at least one blocker code.", nameof(blockerCodes));
        return codes;
    }

    private static AuditEvent ModuleBlocked(string action, string moduleId, ModuleLifecycleState state, IReadOnlyList<string> blockerCodes) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.Module, moduleId),
            AuditOutcomeCodes.Blocked,
            Fields(("state", ModuleStateValue(state)), ("blockers", BlockerCodes(blockerCodes))),
            ModuleRelated(moduleId));
}
