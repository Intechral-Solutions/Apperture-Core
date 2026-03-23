using System.Net;
using System.Net.Http;
using Apperture.Management.Client;

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
        State = SessionLoadState.Loading;
        Notify();
        try
        {
            var me = await _client.GetMeAsync(cancellationToken).ConfigureAwait(false);
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

        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
