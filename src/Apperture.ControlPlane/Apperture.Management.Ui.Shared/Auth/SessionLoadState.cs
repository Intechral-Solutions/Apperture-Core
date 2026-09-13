namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>Session bootstrap state for the Management UI WASM shell.</summary>
public enum SessionLoadState
{
    Unknown,
    Loading,
    Unauthenticated,
    Authenticated,
    ForbiddenUnlinked,
    AccessRequestSession,
    Error
}
