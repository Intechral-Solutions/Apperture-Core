using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Caches merged sign-in options from <c>GET /api/auth/sign-in-options</c> for the WASM shell.</summary>
public sealed class AuthSignInOptionsService
{
    private readonly IManagementClient _client;

    public AuthSignInOptionsService(IManagementClient client) => _client = client;

    public AuthSignInOptionsDto? Current { get; private set; }

    public Exception? LastError { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LastError = null;
            Current = await _client.GetAuthSignInOptionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex;
            Current = null;
        }
    }
}
