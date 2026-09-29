namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>Session bootstrap state for the Management UI WASM shell.</summary>
public enum SessionLoadState
{
    Unknown,
    Loading,

    /// <summary>The management API positively answered 401: there is no session. The only state that sends the operator to sign-in from the gate.</summary>
    Unauthenticated,
    Authenticated,

    /// <summary>
    /// A session exists but <c>auth/me</c> answered 403: the principal does not map to a usable Apperture
    /// account. Terminal; the protected shell never mounts (design gate D5).
    /// </summary>
    AccountUnavailable,
    AccessRequestSession,

    /// <summary>
    /// The management API could not be reached, or answered 5xx / an unexpected status, when the session was
    /// checked. Availability, not authentication: never a sign-in redirect (design gate D8).
    /// </summary>
    ServiceUnavailable,

    /// <summary>
    /// A signed-in tab lost its session (confirmed by <c>auth/me</c> = 401). Session recovery owns the
    /// transition to sign-in; the gate only shows the recovery surface.
    /// </summary>
    SessionExpired
}
