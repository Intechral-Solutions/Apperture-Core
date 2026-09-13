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
                or SessionLoadState.ForbiddenUnlinked
                or SessionLoadState.Loading)
        {
            return false;
        }

        return state switch
        {
            SessionLoadState.Unknown or SessionLoadState.Loading => true,
            SessionLoadState.Unauthenticated or SessionLoadState.Error => true,
            // Wait for App bootstrap (theme/session/TZ) AND capabilities before first mount.
            SessionLoadState.Authenticated => !capabilitiesReady || !appBootstrapReady,
            SessionLoadState.ForbiddenUnlinked => !appBootstrapReady,
            _ => true
        };
    }

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
        || state is SessionLoadState.Unauthenticated or SessionLoadState.Error or SessionLoadState.AccessRequestSession;

    public static string GetBlockingMessage(SessionLoadState state, bool capabilitiesReady)
    {
        if (state is SessionLoadState.Unknown or SessionLoadState.Loading)
            return "Loading session…";

        if (state is SessionLoadState.Unauthenticated or SessionLoadState.Error or SessionLoadState.AccessRequestSession)
            return "Redirecting to sign-in…";

        if (state == SessionLoadState.Authenticated && !capabilitiesReady)
            return "Loading permissions…";

        if (state == SessionLoadState.Authenticated)
            return "Opening console…";

        return "Loading…";
    }

    public static bool ShouldRedirectToLogin(SessionLoadState state, bool isPublicPath) =>
        !isPublicPath && state is SessionLoadState.Unauthenticated or SessionLoadState.Error;

    public static bool ShouldRedirectToAccessRequest(SessionLoadState state, bool isAccessRequestPath) =>
        !isAccessRequestPath && state == SessionLoadState.AccessRequestSession;

    public static bool ShouldRedirectAuthenticatedAwayFromLogin(SessionLoadState state, bool isLoginChooserPath) =>
        isLoginChooserPath && state == SessionLoadState.Authenticated;

    public static bool ShouldRedirectRootBootstrap(SessionLoadState state, bool isRootBootstrapPath) =>
        isRootBootstrapPath && state is SessionLoadState.Authenticated
            or SessionLoadState.ForbiddenUnlinked
            or SessionLoadState.Unauthenticated
            or SessionLoadState.AccessRequestSession
            or SessionLoadState.Error;

    /// <summary>Whether authenticated shell services (menus, dashboard) may start loading.</summary>
    public static bool MayLoadAuthenticatedShellData(SessionLoadState state) =>
        state is SessionLoadState.Authenticated or SessionLoadState.ForbiddenUnlinked;
}
