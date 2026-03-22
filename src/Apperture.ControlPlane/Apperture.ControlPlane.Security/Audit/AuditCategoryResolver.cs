namespace Apperture.ControlPlane.Security.Audit;

/// <summary>
/// Maps audit action names to operator-friendly categories. Epic 32.
/// Does not replace action names; adds grouping for display/filter.
/// </summary>
public static class AuditCategoryResolver
{
    public const string CategoryLifecycle = "lifecycle";
    public const string CategoryScheduler = "scheduler";
    public const string CategoryRecovery = "recovery";
    public const string CategoryModuleLifecycle = "module_lifecycle";
    public const string CategoryGovernance = "governance";
    public const string CategoryAuth = "auth";
    public const string CategoryOther = "other";

    /// <summary>
    /// Resolves the category for an action name.
    /// </summary>
    public static string Resolve(string action)
    {
        if (string.IsNullOrEmpty(action))
            return CategoryOther;

        if (action.StartsWith("execution.", StringComparison.Ordinal)
            || action.StartsWith("runtime.composition.", StringComparison.Ordinal))
            return CategoryLifecycle;

        if (action.StartsWith("scheduler.", StringComparison.Ordinal))
            return CategoryScheduler;

        if (action.StartsWith("recovery.", StringComparison.Ordinal))
            return CategoryRecovery;

        if (action.StartsWith("module.lifecycle.", StringComparison.Ordinal))
            return CategoryModuleLifecycle;

        if (action.StartsWith("governance.", StringComparison.Ordinal)
            || action.StartsWith("audit.read.", StringComparison.Ordinal))
            return CategoryGovernance;

        if (action.StartsWith("auth.", StringComparison.Ordinal)
            || action.StartsWith("user.", StringComparison.Ordinal)
            || action.StartsWith("credential.", StringComparison.Ordinal)
            || action.StartsWith("identity_link.", StringComparison.Ordinal)
            || action.StartsWith("auth_admin.", StringComparison.Ordinal))
            return CategoryAuth;

        return CategoryOther;
    }
}
