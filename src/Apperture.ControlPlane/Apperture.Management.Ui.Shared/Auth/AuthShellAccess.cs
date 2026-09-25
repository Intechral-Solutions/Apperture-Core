namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>
/// Decides when the authenticated Management shell (MainLayout / top bar / menus) may render.
/// UIX-2: once the router child has been shown for a protected session, do not unmount it for
/// Loading / capability re-warms — that dispose-during-await causes parentNode / orphaned components.
/// </summary>
public static class AuthShellAccess
{
    /// <summary>
    /// When true, <c>AuthRouteGate</c> must not render the router child content.
    /// </summary>
    public static bool ShouldBlockProtectedShell(
        SessionLoadState state,
        bool capabilitiesReady,
        bool isPublicPath,
        bool isRootBootstrapPath = false,
        bool shellAlreadyCommitted = false,
        bool appBootstrapReady = true,
        bool isLoginChooserPath = false)
    {
        // Root "/" only hosts a bounce to Control Center — never mount MainLayout there.
        if (isRootBootstrapPath)
            return true;

        if (isPublicPath)
        {
            // After sign-in, stay on the minimal surface until navigation leaves /login.
            // Account-action pages (verify / setup / reset) remain usable while signed in.
            return isLoginChooserPath && state is SessionLoadState.Authenticated;
        }

        // Protected path: once shell was shown, keep it through Loading / cap re-warm.
        if (shellAlreadyCommitted
            && state is SessionLoadState.Authenticated
                or SessionLoadState.Loading)
        {
            return false;
        }

        return state switch
        {
            // Wait for App bootstrap (theme/session/TZ) AND capabilities before first mount.
            SessionLoadState.Authenticated => !capabilitiesReady || !appBootstrapReady,
            // Everything else (loading, signed out, expired, account/service unavailable) has no shell:
            // the gate shows a loading message, a redirect message or a session-status surface instead.
            _ => true
        };
    }

    /// <summary>
    /// The protected shell is replaced by a session-status surface (session-expired recovery, account
    /// unavailable, service unavailable) instead of a plain loading message. Never on public routes.
    /// </summary>
    public static bool ShouldShowSessionStatusSurface(SessionLoadState state, bool isPublicPath) =>
        !isPublicPath
        && state is SessionLoadState.SessionExpired
            or SessionLoadState.AccountUnavailable
            or SessionLoadState.ServiceUnavailable;

    /// <summary>
    /// Whether the gate should remember that the protected shell has been shown this circuit.
    /// </summary>
    public static bool ShouldCommitShell(
        bool currentlyBlocking,
        bool isPublicPath,
        bool isRootBootstrapPath) =>
        !currentlyBlocking && !isPublicPath && !isRootBootstrapPath;

    /// <summary>
    /// Clear sticky shell when returning to login / signed-out surfaces.
    /// </summary>
    public static bool ShouldReleaseShellCommit(
        SessionLoadState state,
        bool isPublicPath) =>
        isPublicPath
        || state is SessionLoadState.Unauthenticated
            or SessionLoadState.AccessRequestSession
            or SessionLoadState.ServiceUnavailable
            or SessionLoadState.SessionExpired
            or SessionLoadState.AccountUnavailable;

    public static string GetBlockingMessage(SessionLoadState state, bool capabilitiesReady)
    {
        if (state is SessionLoadState.Unknown or SessionLoadState.Loading)
            return "Loading session…";

        if (state is SessionLoadState.Unauthenticated or SessionLoadState.AccessRequestSession)
            return "Redirecting to sign-in…";

        if (state == SessionLoadState.SessionExpired)
            return "Your session has expired. Redirecting to sign-in…";

        if (state == SessionLoadState.Authenticated && !capabilitiesReady)
            return "Loading permissions…";

        if (state == SessionLoadState.Authenticated)
            return "Opening console…";

        return "Loading…";
    }

    /// <summary>
    /// Boot-time sign-in redirect: only positive evidence of no session (<c>auth/me</c> = 401). An outage
    /// (<see cref="SessionLoadState.ServiceUnavailable"/>) or an unusable account never qualifies, and a
    /// session lost while the tab is open is owned by session recovery, not by this rule.
    /// </summary>
    public static bool ShouldRedirectToLogin(SessionLoadState state, bool isPublicPath) =>
        !isPublicPath && state == SessionLoadState.Unauthenticated;

    public static bool ShouldRedirectToAccessRequest(SessionLoadState state, bool isAccessRequestPath) =>
        !isAccessRequestPath && state == SessionLoadState.AccessRequestSession;

    public static bool ShouldRedirectAuthenticatedAwayFromLogin(SessionLoadState state, bool isLoginChooserPath) =>
        isLoginChooserPath && state == SessionLoadState.Authenticated;

    public static bool ShouldRedirectRootBootstrap(SessionLoadState state, bool isRootBootstrapPath) =>
        isRootBootstrapPath && state is SessionLoadState.Authenticated
            or SessionLoadState.Unauthenticated
            or SessionLoadState.AccessRequestSession;

    /// <summary>Whether authenticated shell services (menus, dashboard) may start loading.</summary>
    public static bool MayLoadAuthenticatedShellData(SessionLoadState state) =>
        state == SessionLoadState.Authenticated;
}
