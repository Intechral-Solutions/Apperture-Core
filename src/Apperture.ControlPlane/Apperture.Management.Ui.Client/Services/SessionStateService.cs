using System.Net;
using System.Net.Http;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared.Auth;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Loads <see cref="MeResponse"/> from the API for shell presence. Registered <c>AddScoped</c>; the only
/// host that actually creates a DI scope around it is the WASM client, where that scope spans the whole
/// browser tab's lifetime, not a per-request scope -- so consumers with their own longer-lived state
/// (e.g. <see cref="NotificationToastWatcher"/>) can safely also be <c>AddScoped</c> against it.
/// </summary>
public sealed class SessionStateService
{
    private readonly IManagementClient _client;

    public SessionStateService(IManagementClient client) => _client = client;

    public SessionLoadState State { get; private set; } = SessionLoadState.Unknown;

    public MeResponse? User { get; private set; }

    public string? ErrorMessage { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Raised when a refresh started while signed in is answered 401 by <c>auth/me</c>. <c>auth/me</c> is not reported
    /// by the HTTP pipeline (it is the boot path), so this is how that suspicion reaches session recovery.
    /// </summary>
    public event Action? UnauthorizedDuringRefresh;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Soft refresh when already signed in: skip Loading notify so AuthRouteGate does not unmount
        // the Router/MainLayout mid-flight (Symbol()/parentNode while menus or pages load).
        var softRefresh = State == SessionLoadState.Authenticated;
        if (!softRefresh)
        {
            State = SessionLoadState.Loading;
            Notify();
        }

        var previousState = State;
        var previousUserId = User?.UserId;
        var previousError = ErrorMessage;

        // The outcome is computed into locals and applied only after every await, so a refresh that was in flight
        // while session recovery marked the session cannot overwrite that decision.
        SessionLoadState outcome;
        MeResponse? outcomeUser = null;
        string? outcomeError = null;
        try
        {
            // Stay on the Blazor sync context: Notify() and AuthRouteGate UpdateBlocking must not
            // race the renderer (ConfigureAwait(false) → parentNode / orphaned components).
            var me = await _client.GetMeAsync(cancellationToken);
            if (me is null)
            {
                if (softRefresh)
                {
                    // A signed-in tab was told 401 by auth/me. Whether that is session loss is recovery's
                    // decision (one probe, one navigation, loop breaker), never this refresh's: it neither
                    // declares the tab signed out nor navigates.
                    if (State == SessionLoadState.Authenticated)
                        UnauthorizedDuringRefresh?.Invoke();
                    return;
                }

                var access = await _client.GetAccessRequestSessionAsync(cancellationToken);
                outcome = access is null
                    ? SessionLoadState.Unauthenticated
                    : SessionLoadState.AccessRequestSession;
            }
            else
            {
                outcome = SessionLoadState.Authenticated;
                outcomeUser = me;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            outcome = SessionLoadState.AccountUnavailable;
            outcomeError = ex.Message;
        }
        catch (HttpRequestException)
        {
            // Connection refused, 5xx, unexpected status: availability, never "signed out" (design gate D8).
            outcome = SessionLoadState.ServiceUnavailable;
            outcomeError = "Unable to reach the management API.";
        }
        catch (Exception ex)
        {
            outcome = SessionLoadState.ServiceUnavailable;
            outcomeError = ex.Message;
        }

        // A signed-in tab whose state was marked (expired, account/service unavailable) while this refresh was in
        // flight keeps the mark: recovery already owns that transition.
        if (softRefresh && State != SessionLoadState.Authenticated)
            return;

        User = outcomeUser;
        ErrorMessage = outcomeError;
        State = outcome;

        // Soft refresh with identical outcomes must not re-SHC the shell (MainLayout/NavRail).
        if (softRefresh
            && previousState == State
            && previousUserId == User?.UserId
            && previousError == ErrorMessage)
        {
            return;
        }

        Notify();
    }

    /// <summary>
    /// Session recovery confirmed (by <c>auth/me</c> = 401) that this tab's session is gone. Drops the identity so
    /// nothing keeps treating it as valid; <see cref="Changed"/> stops the Toast watcher and lets the gate replace
    /// the shell with the recovery surface. The navigation to sign-in is the coordinator's, not this state's.
    /// </summary>
    public void MarkSessionExpired() => Mark(SessionLoadState.SessionExpired, null);

    /// <summary><c>auth/me</c> = 403 while signed in: the principal no longer maps to a usable account. Terminal.</summary>
    public void MarkAccountUnavailable() => Mark(SessionLoadState.AccountUnavailable, "Authenticated principal is not linked to a platform user.");

    /// <summary>The session check could not be completed (outage, 5xx, timeout). Retryable; never a sign-in.</summary>
    public void MarkServiceUnavailable() => Mark(SessionLoadState.ServiceUnavailable, "Unable to reach the management API.");

    private void Mark(SessionLoadState state, string? message)
    {
        User = null;
        ErrorMessage = message;
        State = state;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
