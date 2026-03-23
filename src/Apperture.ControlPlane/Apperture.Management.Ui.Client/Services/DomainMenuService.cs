using Apperture.Management.Ui.Shared.Models;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Service for loading and managing Control Panel navigation menus.
/// Loads from configured source (API endpoint), caches in memory.
/// </summary>
public class DomainMenuService
{
    private readonly HttpClient _http;
    private readonly string _jsonSource;
    private readonly ILogger<DomainMenuService> _logger;

    private Dictionary<string, DomainMenu> _menus = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastLoaded;

    /// <summary>
    /// Serializes menu loads so concurrent callers (e.g. NavRail init + MainLayout hamburger)
    /// all await the same fetch. A plain "if loading return" caused the second caller to finish
    /// immediately with empty menus while the first was still in flight.
    /// </summary>
    private readonly SemaphoreSlim _menuLoadLock = new(1, 1);

    public DomainMenuService(HttpClient http, IOptions<ControlPanelMenuOptions> options, ILogger<DomainMenuService> logger)
    {
        _http = http;
        _jsonSource = options.Value.JsonSource;
        _logger = logger;
    }

    /// <summary>
    /// Loads menus from the configured source. Uses in-memory caching.
    /// </summary>
    public async Task LoadMenusAsync(bool forceReload = false)
    {
        await _menuLoadLock.WaitAsync();
        try
        {
            if (forceReload)
            {
                _menus = new(StringComparer.OrdinalIgnoreCase);
                _lastLoaded = null;
            }

            if (_menus.Count > 0 && !forceReload)
                return;

            // Fetch and deserialize explicitly so we can fully control the JSON options and
            // reliably detect "successful but useless" responses (e.g., domain entries that
            // deserialize but end up with empty `Items` lists).
            var json = await _http.GetStringAsync(_jsonSource);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var dict = JsonSerializer.Deserialize<Dictionary<string, DomainMenu>>(json, options);

            var hasUsableItems =
                dict is not null &&
                dict.Count > 0 &&
                dict.Values.Any(d => d is not null && d.Items.Count > 0);

            if (hasUsableItems && dict is not null)
            {
                _menus = dict;
                _lastLoaded = DateTime.UtcNow;
                _logger.LogInformation("Loaded {Count} domain menus: {Domains}",
                    _menus.Count,
                    string.Join(", ", _menus.Keys));
            }
            else
            {
                // The endpoint can return a structurally valid JSON payload, but if `Items`
                // is empty due to deserialization mismatches we end up with no top-level icons.
                // Treat that as a failed load and fall back to our default menu definitions.
                _logger.LogWarning("Menus response from {Source} did not contain usable items; falling back to defaults.", _jsonSource);
                _menus = ControlPanelMenuDefinitions.GetDefaultMenus();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error loading menus from {Source}: {StatusCode}",
                _jsonSource, ex.StatusCode);

            if (_menus.Count == 0)
            {
                _menus = ControlPanelMenuDefinitions.GetDefaultMenus();
                _logger.LogWarning("Using fallback menus due to load failure");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading menus from {Source}", _jsonSource);

            if (_menus.Count == 0)
            {
                _menus = ControlPanelMenuDefinitions.GetDefaultMenus();
                _logger.LogWarning("Using fallback menus due to error");
            }
        }
        finally
        {
            _menuLoadLock.Release();
        }
    }

    /// <summary>
    /// Clears the menu cache.
    /// </summary>
    public void InvalidateCache()
    {
        _menus = new();
        _lastLoaded = null;
        _logger.LogInformation("Menu cache invalidated");
    }

    public DateTime? LastLoaded => _lastLoaded;

    public IEnumerable<string> GetValidDomains() => _menus.Keys;

    /// <summary>
    /// Domains to render in the middle dynamic rail. Excludes Dashboard and Config (custom zone owners).
    /// </summary>
    public IEnumerable<string> GetMiddleDomains() =>
        SectionOwnerConstants.GetMiddleDomains(_menus.Keys);

    public DomainMenu? GetDomain(string domainName) =>
        _menus.TryGetValue(domainName, out var def) ? def : null;
}
