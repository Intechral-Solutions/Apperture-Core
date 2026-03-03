using Apperture.Management.Ui.Shared.Models;
using Apperture.Management.Ui.Shared.Options;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

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
    private bool _isLoading;

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
        if (_menus.Count > 0 && !forceReload) return;
        if (_isLoading) return;

        _isLoading = true;
        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, DomainMenu>>(_jsonSource);

            if (dict != null && dict.Count > 0)
            {
                _menus = dict;
                _lastLoaded = DateTime.UtcNow;
                _logger.LogInformation("Loaded {Count} domain menus: {Domains}",
                    _menus.Count,
                    string.Join(", ", _menus.Keys));
            }
            else
            {
                _logger.LogWarning("No menus returned from {Source}", _jsonSource);
                _menus = new();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error loading menus from {Source}: {StatusCode}",
                _jsonSource, ex.StatusCode);

            if (_menus.Count == 0)
            {
                _menus = GetFallbackMenus();
                _logger.LogWarning("Using fallback menus due to load failure");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading menus from {Source}", _jsonSource);

            if (_menus.Count == 0)
            {
                _menus = GetFallbackMenus();
                _logger.LogWarning("Using fallback menus due to error");
            }
        }
        finally
        {
            _isLoading = false;
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

    public DomainMenu? GetDomain(string domainName) =>
        _menus.TryGetValue(domainName, out var def) ? def : null;

    private static Dictionary<string, DomainMenu> GetFallbackMenus()
    {
        return new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase)
        {
            ["Catalog"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Browse Capabilities", Href = "/catalog" }
                }
            }
        };
    }
}
