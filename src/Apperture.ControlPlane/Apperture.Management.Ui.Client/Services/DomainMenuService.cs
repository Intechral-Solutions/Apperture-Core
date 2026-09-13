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

    // Seeded so NavRail first paint has stable @key domains (no empty→full remount).
    private Dictionary<string, DomainMenu> _menus =
        ControlPanelMenuDefinitions.GetDefaultMenus();
    private DateTime? _lastLoaded;
    private bool _hasFetchedFromSource;

    /// <summary>
    /// Serializes menu loads so concurrent callers (e.g. MainLayout kickoff + hamburger)
    /// all await the same fetch.
    /// </summary>
    private readonly SemaphoreSlim _menuLoadLock = new(1, 1);
    private readonly TaskCompletionSource _firstFetchTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Raised after menus are loaded or the cache is cleared so shell chrome can rebuild.</summary>
    public event Action? Changed;

    /// <summary>
    /// Completes after the first <see cref="LoadMenusAsync"/> attempt finishes (success or fallback).
    /// Pages await this before their own HTTP so Body SHC does not race the shell menu wave.
    /// </summary>
    public Task WhenFetchedAsync() => _firstFetchTcs.Task;

    public DomainMenuService(HttpClient http, IOptions<ControlPanelMenuOptions> options, ILogger<DomainMenuService> logger)
    {
        _http = http;
        _jsonSource = options.Value.JsonSource;
        _logger = logger;
    }

    /// <summary>
    /// Loads menus from the configured source. Uses in-memory caching.
    /// Seeded defaults do not count as a completed fetch — first call still hits the API.
    /// </summary>
    public async Task LoadMenusAsync(bool forceReload = false)
    {
        var notify = false;
        await _menuLoadLock.WaitAsync();
        try
        {
            if (forceReload)
            {
                _menus = ControlPanelMenuDefinitions.GetDefaultMenus();
                _lastLoaded = null;
                _hasFetchedFromSource = false;
            }

            // Real cache hit (API or fallback already applied) — do not re-notify.
            if (_hasFetchedFromSource && _menus.Count > 0 && !forceReload)
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
                notify = !MiddleDomainsEqual(_menus, dict);
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
                var defaults = ControlPanelMenuDefinitions.GetDefaultMenus();
                notify = !MiddleDomainsEqual(_menus, defaults);
                _menus = defaults;
            }

            _hasFetchedFromSource = true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error loading menus from {Source}: {StatusCode}",
                _jsonSource, ex.StatusCode);

            if (!_hasFetchedFromSource)
            {
                var defaults = ControlPanelMenuDefinitions.GetDefaultMenus();
                notify = !MiddleDomainsEqual(_menus, defaults);
                _menus = defaults;
                _logger.LogWarning("Using fallback menus due to load failure");
            }

            _hasFetchedFromSource = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading menus from {Source}", _jsonSource);

            if (!_hasFetchedFromSource)
            {
                var defaults = ControlPanelMenuDefinitions.GetDefaultMenus();
                notify = !MiddleDomainsEqual(_menus, defaults);
                _menus = defaults;
                _logger.LogWarning("Using fallback menus due to error");
            }

            _hasFetchedFromSource = true;
        }
        finally
        {
            _menuLoadLock.Release();
        }

        _firstFetchTcs.TrySetResult();

        if (!notify)
            return;

        // Defer so subscribers are not inside LoadMenusAsync / OnAfterRender when they SHC.
        await Task.Yield();
        Changed?.Invoke();
    }

    /// <summary>
    /// Clears the menu cache back to seeded defaults (keeps stable nav keys).
    /// </summary>
    public void InvalidateCache()
    {
        _menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        _lastLoaded = null;
        _hasFetchedFromSource = false;
        _logger.LogInformation("Menu cache invalidated");
        Changed?.Invoke();
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

    private static bool MiddleDomainsEqual(
        IReadOnlyDictionary<string, DomainMenu> left,
        IReadOnlyDictionary<string, DomainMenu> right)
    {
        var leftKeys = SectionOwnerConstants.GetMiddleDomains(left.Keys)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rightKeys = SectionOwnerConstants.GetMiddleDomains(right.Keys)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (leftKeys.Length != rightKeys.Length)
            return false;

        for (var i = 0; i < leftKeys.Length; i++)
        {
            if (!string.Equals(leftKeys[i], rightKeys[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
