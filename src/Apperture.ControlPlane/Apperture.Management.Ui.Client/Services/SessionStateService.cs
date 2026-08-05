using System.Net;
using System.Net.Http;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared.Auth;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Loads <see cref="MeResponse"/> from the API for shell presence. Scoped per WASM circuit.</summary>
public sealed class SessionStateService
{
    private readonly IManagementClient _client;

    public SessionStateService(IManagementClient client) => _client = client;

    public SessionLoadState State { get; private set; } = SessionLoadState.Unknown;

    public MeResponse? User { get; private set; }

    public string? ErrorMessage { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Soft refresh when already signed in: skip Loading notify so AuthRouteGate does not unmount
        // the Router/MainLayout mid-flight (Symbol()/parentNode while menus or pages load).
        var softRefresh = State is SessionLoadState.Authenticated or SessionLoadState.ForbiddenUnlinked;
        if (!softRefresh)
        {
            State = SessionLoadState.Loading;
            Notify();
        }

        var previousState = State;
        var previousUserId = User?.UserId;
        var previousError = ErrorMessage;

        try
        {
            // Stay on the Blazor sync context: Notify() and AuthRouteGate UpdateBlocking must not
            // race the renderer (ConfigureAwait(false) → parentNode / orphaned components).
            var me = await _client.GetMeAsync(cancellationToken);
            if (me is null)
            {
                User = null;
                State = SessionLoadState.Unauthenticated;
                ErrorMessage = null;
            }
            else
            {
                User = me;
                State = SessionLoadState.Authenticated;
                ErrorMessage = null;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            User = null;
            State = SessionLoadState.ForbiddenUnlinked;
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException)
        {
            User = null;
            State = SessionLoadState.Error;
            ErrorMessage = "Unable to reach the management API.";
        }
        catch (Exception ex)
        {
            User = null;
            State = SessionLoadState.Error;
            ErrorMessage = ex.Message;
        }

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

    private void Notify() => Changed?.Invoke();
}
