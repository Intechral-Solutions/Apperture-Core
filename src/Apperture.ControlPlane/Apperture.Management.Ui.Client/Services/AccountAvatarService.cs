using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Holds the operator's current avatar for the shell and account surfaces.
/// </summary>
/// <remarks>
/// Custom photos are fetched through <see cref="IManagementClient"/> and held as a data URL rather
/// than pointed at with an image element. The API host is a different origin from the UI host in
/// development, and a plain image request would not carry the authentication cookie; going through the
/// API client keeps the request authenticated everywhere the app runs.
///
/// The fetched bytes are cached against the avatar's version token, so re-rendering the shell costs
/// nothing and changing the avatar produces a new token and therefore a fresh fetch.
/// </remarks>
public sealed class AccountAvatarService : IDisposable
{
    private readonly IManagementClient _client;
    private readonly SessionStateService _session;

    private string? _cachedVersion;
    private string? _cachedDataUrl;
    private Task? _inFlight;

    public AccountAvatarService(IManagementClient client, SessionStateService session)
    {
        _client = client;
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Current avatar descriptor, or null before the session resolves.</summary>
    public AccountAvatarDto? Avatar { get; private set; }

    /// <summary>Data URL for a custom photo, or null when the built-in artwork should be drawn.</summary>
    public string? CustomImageDataUrl => _cachedDataUrl;

    /// <summary>Raised when the avatar descriptor or its loaded image changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the custom photo if the current avatar has one and it is not already cached. Safe to call
    /// from every render path: concurrent callers share one request.
    /// </summary>
    public Task EnsureImageLoadedAsync(CancellationToken cancellationToken = default)
    {
        Avatar ??= _session.User?.Avatar;

        if (Avatar is null || !Avatar.IsCustom)
        {
            ClearImage();
            return Task.CompletedTask;
        }

        if (string.Equals(_cachedVersion, Avatar.Version, StringComparison.Ordinal) && _cachedDataUrl is not null)
            return Task.CompletedTask;

        return _inFlight ??= LoadImageAsync(Avatar, cancellationToken);
    }

    /// <summary>
    /// Adopts a descriptor returned from a save, so the shell updates immediately instead of waiting
    /// for the next session refresh.
    /// </summary>
    public async Task ApplyAsync(AccountAvatarDto avatar, CancellationToken cancellationToken = default)
    {
        Avatar = avatar;
        if (!avatar.IsCustom)
        {
            ClearImage();
            Notify();
            return;
        }

        _cachedVersion = null;
        _cachedDataUrl = null;
        _inFlight = null;
        await EnsureImageLoadedAsync(cancellationToken);
        Notify();
    }

    private async Task LoadImageAsync(AccountAvatarDto avatar, CancellationToken cancellationToken)
    {
        try
        {
            var image = await _client.GetAccountAvatarImageAsync(
                AccountAvatarDto.DefaultImageSize,
                avatar.Version,
                cancellationToken);

            if (image is null || image.Content.Length == 0)
            {
                // Descriptor says custom but no rendition came back. Falling back to built-in artwork
                // is the documented behavior for a missing image, and avoids a broken avatar.
                ClearImage();
            }
            else
            {
                _cachedVersion = avatar.Version;
                _cachedDataUrl = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Content)}";
            }
        }
        catch (HttpRequestException)
        {
            ClearImage();
        }
        finally
        {
            _inFlight = null;
        }

        Notify();
    }

    private void OnSessionChanged()
    {
        var fromSession = _session.User?.Avatar;
        if (fromSession is null)
        {
            Avatar = null;
            ClearImage();
            Notify();
            return;
        }

        if (Avatar is not null
            && string.Equals(Avatar.Version, fromSession.Version, StringComparison.Ordinal)
            && string.Equals(Avatar.Kind, fromSession.Kind, StringComparison.Ordinal))
        {
            return;
        }

        Avatar = fromSession;
        _cachedVersion = null;
        _cachedDataUrl = null;
        _inFlight = null;
        Notify();
    }

    private void ClearImage()
    {
        _cachedVersion = null;
        _cachedDataUrl = null;
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
