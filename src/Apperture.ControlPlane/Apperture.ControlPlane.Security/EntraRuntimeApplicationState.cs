using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Compares the active Entra registration to process-level <see cref="EntraAuthOptions"/>
/// (already overlaid at host start) to decide whether operators still need a restart.
/// </summary>
public static class EntraRuntimeApplicationState
{
    public static bool MayRequireRestartToApplyChanges(
        EntraInstanceRegistration? active,
        EntraAuthOptions effective)
    {
        if (active is not { Enabled: true })
            return false;
        if (effective is null || !effective.Enabled)
            return true;
        if (!AuthoritiesEqual(active.Authority, effective.Authority))
            return true;
        if (!string.Equals(active.ClientId.Trim(), effective.ClientId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(active.CallbackPath)
            && !string.Equals(active.CallbackPath.Trim(), effective.CallbackPath.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(effective.ClientSecret))
            return true;
        if (!string.Equals(
                (active.AuthenticationContextId ?? "").Trim(),
                (effective.AuthenticationContextId ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static string Describe(bool mayRequireRestart, bool hasActiveRegistration) =>
        mayRequireRestart
            ? "Pending restart/reload confirmation: the active registration is persisted and used by API-derived state, but OpenID Connect handler behavior may lag until host restart."
            : hasActiveRegistration
                ? "The active Entra registration is applied to this host process."
                : "No active Entra registration override is currently pending application.";

    private static bool AuthoritiesEqual(string? left, string? right) =>
        string.Equals((left ?? "").Trim().TrimEnd('/'), (right ?? "").Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
