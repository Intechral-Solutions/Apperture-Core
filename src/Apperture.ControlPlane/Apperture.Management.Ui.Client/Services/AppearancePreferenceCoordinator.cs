using System.Net.Http;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Microsoft.Extensions.Logging;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Reconciles the appearance the operator stored on their account with the one this browser is using.
/// </summary>
/// <remarks>
/// Precedence is documented on <see cref="AppearancePreferenceResolution"/>: the account preference
/// wins over the browser's local value, which wins over the platform default.
///
/// Applying the account preference through <see cref="ThemeService"/> also writes it to local storage.
/// That is deliberate: the host page's pre-WebAssembly boot script reads local storage, so mirroring
/// the account value there is what stops a signed-in operator from seeing a flash of the wrong theme
/// on their next visit, before the session is known.
///
/// When the operator has no stored account preference, whatever they had chosen in this browser is
/// promoted to their account on first sign-in, so an existing local choice is never silently lost.
/// </remarks>
public sealed class AppearancePreferenceCoordinator : IDisposable
{
    private readonly SessionStateService _session;
    private readonly ThemeService _theme;
    private readonly IManagementClient _client;
    private readonly ILogger<AppearancePreferenceCoordinator> _logger;

    private string? _reconciledForUserId;

    public AppearancePreferenceCoordinator(
        SessionStateService session,
        ThemeService theme,
        IManagementClient client,
        ILogger<AppearancePreferenceCoordinator> logger)
    {
        _session = session;
        _theme = theme;
        _client = client;
        _logger = logger;
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Last resolved source, for diagnostics and the preferences page.</summary>
    public AppearancePreferenceSource Source { get; private set; } = AppearancePreferenceSource.Browser;

    /// <summary>
    /// Applies the signed-in operator's stored appearance. Called once during shell bootstrap, after
    /// the session and theme service have both initialized.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var user = _session.User;
        if (user is null)
        {
            Source = AppearancePreferenceSource.Browser;
            return;
        }

        // Guard against re-running for the same operator when the session notifies more than once.
        if (string.Equals(_reconciledForUserId, user.UserId, StringComparison.Ordinal))
            return;
        _reconciledForUserId = user.UserId;

        var browserValue = ThemePreferenceParser.ToStorageValue(_theme.Current);
        var (effective, source) = AppearancePreferenceResolution.Resolve(user.Appearance, browserValue);
        Source = source;

        if (source == AppearancePreferenceSource.AccountPreference)
        {
            if (effective != _theme.Current)
                await _theme.SetPreferenceAsync(effective);
            return;
        }

        // No account preference yet: adopt what this browser was already using so the operator's
        // existing choice survives their first sign-in on a new deployment.
        await PromoteBrowserPreferenceAsync(effective, cancellationToken);
    }

    private async Task PromoteBrowserPreferenceAsync(
        ThemePreference preference,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await _client.GetAccountPreferencesAsync(cancellationToken);
            await _client.UpdateAccountPreferencesAsync(
                new UpdateUserPreferencesRequestDto
                {
                    Appearance = ThemePreferenceParser.ToStorageValue(preference),
                    TimeFormat = current.TimeFormat,
                    TimeZoneId = current.TimeZoneId
                },
                cancellationToken);
            Source = AppearancePreferenceSource.AccountPreference;
        }
        catch (HttpRequestException ex)
        {
            // Not being able to save is not worth interrupting sign-in over; the browser preference
            // still applies and the preferences page can save it explicitly later.
            _logger.LogDebug(ex, "Could not promote this browser's appearance preference to the account.");
        }
    }

    private void OnSessionChanged()
    {
        if (_session.User is null)
        {
            _reconciledForUserId = null;
            Source = AppearancePreferenceSource.Browser;
        }
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
