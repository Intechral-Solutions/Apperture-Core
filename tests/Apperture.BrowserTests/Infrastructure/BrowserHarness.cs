using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// One shared stack for the whole browser suite (xunit collection fixture): the harness SQL database, the
/// stub OIDC provider, the attacker origins, the real Management.Host (in-process, real Kestrel port), the
/// real Ui.Server (child process) and Playwright. Started once, torn down once; per-test state is reset by
/// <see cref="ResetAsync"/>. Does nothing when the suite is not enabled.
/// </summary>
public sealed class BrowserHarness : IAsyncLifetime
{
    public const string OperatorUsername = "harness-operator";
    public const string OperatorDisplayName = "Test Operator";

    private readonly Dictionary<BrowserEngine, IBrowser> _browsers = new();
    private readonly SemaphoreSlim _browserGate = new(1, 1);
    private IPlaywright? _playwright;
    private string? _connectionString;

    public bool Started { get; private set; }

    public HarnessOrigins Origins { get; private set; } = null!;

    public LiveManagementHost Host { get; private set; } = null!;

    public UiServerProcess Ui { get; private set; } = null!;

    public StubOidcProvider Oidc { get; private set; } = null!;

    public StubOidcSettings OidcSettings { get; private set; } = null!;

    public AttackerHost Attacker { get; private set; } = null!;

    public HarnessSeeding Seeding { get; private set; } = null!;

    public RequestRecorder Recorder => Host.Recorder;

    public ControllableTimeProvider Clock => Host.Clock;

    public async Task InitializeAsync()
    {
        if (!BrowserTestGate.Enabled)
            return;

        try
        {
            AcquireRunLock();
            Origins = HarnessOrigins.AllocateDynamic();
            OidcSettings = new StubOidcSettings(Origins.Oidc);

            _connectionString = BrowserSqlConfiguration.ResolveScratchConnectionString();
            await BrowserSqlConfiguration.RecreateScratchDatabaseAsync(_connectionString).ConfigureAwait(false);
            await BrowserSqlConfiguration.MigrateAsync(_connectionString).ConfigureAwait(false);

            Oidc = new StubOidcProvider(OidcSettings);
            await Oidc.StartAsync().ConfigureAwait(false);

            Attacker = new AttackerHost(Origins);
            await Attacker.StartAsync().ConfigureAwait(false);

            Host = new LiveManagementHost(Origins, _connectionString, OidcSettings);
            // Touching Services forces WebApplicationFactory to build and start the host.
            _ = Host.Services;
            await WaitForApiAsync().ConfigureAwait(false);
            AssertRecorderKnowsTheSessionCookie();

            Seeding = new HarnessSeeding(Host.LiveServices);
            await Seeding.CreateLocalAdministratorAsync(OperatorUsername, OperatorDisplayName).ConfigureAwait(false);

            Ui = new UiServerProcess(Origins);
            await Ui.StartAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false);

            _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            Started = true;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        // Every step is independent and best effort: one failure must never skip the remaining cleanup.
        foreach (var browser in _browsers.Values)
            await TryAsync(() => browser.CloseAsync()).ConfigureAwait(false);

        _browsers.Clear();
        await TryAsync(() =>
        {
            _playwright?.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        _playwright = null;

        if (Ui is not null)
            await TryAsync(() => Ui.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (Host is not null)
            await TryAsync(() =>
            {
                Host.Dispose();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        if (Attacker is not null)
            await TryAsync(() => Attacker.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (Oidc is not null)
            await TryAsync(() => Oidc.DisposeAsync().AsTask()).ConfigureAwait(false);

        Started = false;
        ReleaseRunLock();

        // Reported last so cleanup always completes; also catches a violation in the final test of the run.
        AssertNoForeignLocalTraffic();
    }

    private static async Task TryAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch
        {
            // Teardown must never mask a test failure or skip later steps.
        }
    }

    private FileStream? _runLock;

    /// <summary>
    /// The harness database has a fixed name and is dropped on start, so two runs at once (two worktrees, two agents)
    /// would destroy each other's data. An exclusive file lock makes the second run fail fast instead. The OS releases
    /// it if the process dies, so it can never go stale.
    /// </summary>
    private void AcquireRunLock()
    {
        var path = Path.Combine(Path.GetTempPath(), "apperture-browsertests.run.lock");
        try
        {
            _runLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "Another Apperture.BrowserTests run is active on this machine (it owns the shared Apperture_BrowserTests database). " +
                "Wait for it to finish.", ex);
        }
    }

    private void ReleaseRunLock()
    {
        _runLock?.Dispose();
        _runLock = null;
    }

    /// <summary>
    /// Every negative cookie assertion ("the cross-site request carried no session cookie") depends on the recorder
    /// knowing the cookie's real name. If the name ever changes (e.g. a __Host- prefix in SEC-1B3), those assertions
    /// would silently pass on an empty list, so the mismatch is fatal here.
    /// </summary>
    private void AssertRecorderKnowsTheSessionCookie()
    {
        var options = Host.LiveServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        var actual = options.Cookie.Name;
        if (!string.Equals(actual, RequestRecorder.SessionCookieName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The application session cookie is named '{actual}' but the harness recorder looks for '{RequestRecorder.SessionCookieName}'. " +
                "Update RequestRecorder.SessionCookieName, or every 'no session cookie' assertion would pass vacuously.");
        }
    }

    /// <summary>Puts every controllable piece of shared state back to its neutral value before a test.</summary>
    public Task ResetAsync()
    {
        AssertNoForeignLocalTraffic();
        Clock.Reset();
        Recorder.Clear();
        Oidc.Reset();
        Attacker.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Directory object id of the stub-provider administrator (a fictitious identity).</summary>
    public const string EntraObjectId = "3d1b7e42-6a55-4f6e-9c0d-1e2f3a4b5c6d";

    private bool _entraFixtureSeeded;

    /// <summary>
    /// Seeds, once per run, the active Entra registration for the stub provider and an administrator linked
    /// to <see cref="EntraObjectId"/>, and points the stub at that identity. Call from tests that sign in
    /// through the stub.
    /// </summary>
    public async Task EnsureEntraFixtureAsync()
    {
        await _browserGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_entraFixtureSeeded)
            {
                await Seeding.CreateActiveEntraRegistrationAsync(OidcSettings).ConfigureAwait(false);
                await Seeding.CreateEntraAdministratorAsync(OidcSettings, EntraObjectId, "Test Directory Operator").ConfigureAwait(false);
                _entraFixtureSeeded = true;
            }
        }
        finally
        {
            _browserGate.Release();
        }

        Oidc.Identity = new StubOidcIdentity(EntraObjectId, "Test Directory Operator", IncludeAuthenticationContext: true);
    }

    public async Task<IBrowser> GetBrowserAsync(BrowserEngine engine)
    {
        await _browserGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browsers.TryGetValue(engine, out var existing))
                return existing;

            var type = engine switch
            {
                BrowserEngine.Chromium => _playwright!.Chromium,
                BrowserEngine.Firefox => _playwright!.Firefox,
                _ => _playwright!.Webkit,
            };
            var name = engine.ToString().ToLowerInvariant();

            IBrowser browser;
            try
            {
                browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            {
                // First run on this machine: fetch the engine (downloads to the user's Playwright cache, not the repo).
                var exit = Microsoft.Playwright.Program.Main(["install", name]);
                if (exit != 0)
                    throw new InvalidOperationException($"'playwright install {name}' failed with exit code {exit}.", ex);

                browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
            }

            _browsers[engine] = browser;
            return browser;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    /// <summary>
    /// A fresh, isolated browser context (empty cookie jar, no storage). The client's <c>appsettings*.json</c>
    /// is fetched from the real Ui.Server and only its <c>ApiBaseUrl</c> is rewritten to this run's API origin,
    /// because the shipped client resolves a loopback UI to the developer stack's port 9000.
    /// </summary>
    /// <remarks>
    /// <paramref name="interceptClientConfig"/> installs the <c>appsettings*.json</c> route. Pass <c>false</c> for
    /// any test that gathers CORS evidence: while ANY route is registered, Playwright's Chromium answers CORS
    /// preflights itself (they never reach the server), which would falsely make every cross-origin request
    /// look permitted.
    /// </remarks>
    public async Task<IBrowserContext> NewContextAsync(BrowserEngine engine = BrowserEngine.Chromium, bool interceptClientConfig = true)
    {
        var browser = await GetBrowserAsync(engine).ConfigureAwait(false);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        }).ConfigureAwait(false);
        context.SetDefaultTimeout(30_000);
        if (!interceptClientConfig)
            return context;

        var apiBase = Origins.Api + "/api";
        await context.RouteAsync(new Regex(@"/appsettings(\.[A-Za-z]+)?\.json(\?.*)?$"), async route =>
        {
            var response = await route.FetchAsync().ConfigureAwait(false);
            var json = JsonNode.Parse(await response.TextAsync().ConfigureAwait(false)) as JsonObject ?? new JsonObject();
            json["ApiBaseUrl"] = apiBase;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Response = response,
                Body = json.ToJsonString(),
                ContentType = "application/json",
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Safety net, registered last so it wins: nothing the browser does may reach a localhost port that is not
        // one of this run's own hosts. Without it, a client that resolves the API to the developer's Aspire stack
        // (:9000) would silently talk to it. The violation is counted and the request is aborted.
        var allowedPorts = new HashSet<int>
        {
            Origins.UiPort, Origins.ApiPort, Origins.OidcPort, Origins.CrossSiteAttackerPort, Origins.SameSiteAttackerPort,
        };
        await context.RouteAsync(
            url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                   && uri.Scheme is "http" or "https"
                   && uri.Host is "localhost" or "127.0.0.1"
                   && !allowedPorts.Contains(uri.Port),
            async route =>
            {
                Interlocked.Increment(ref _foreignLocalRequests);
                _lastForeignLocalRequest = route.Request.Url;
                await route.AbortAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

        return context;
    }

    private int _foreignLocalRequests;
    private string? _lastForeignLocalRequest;

    /// <summary>Requests aborted because they targeted a localhost port that is not one of this run's hosts.</summary>
    public int ForeignLocalRequests => Volatile.Read(ref _foreignLocalRequests);

    /// <summary>Fails when any browser request tried to reach a host outside the harness (see the safety net in <see cref="NewContextAsync"/>).</summary>
    public void AssertNoForeignLocalTraffic()
    {
        var count = Interlocked.Exchange(ref _foreignLocalRequests, 0);
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"{count} browser request(s) since the last check targeted a localhost port outside the harness (last: {_lastForeignLocalRequest}). " +
                "The harness aborted them; check the client's ApiBaseUrl rewrite. (Checked when the next test starts and when the suite ends; " +
                "contexts created with interceptClientConfig:false have no such guard.)");
        }
    }

    /// <summary>Local sign-in through the real API, sharing the context's cookie jar. Redacts everything.</summary>
    public async Task SignInAsync(IBrowserContext context, string username = OperatorUsername, string password = HarnessSeeding.Passphrase)
    {
        var response = await context.APIRequest.PostAsync(
            Origins.Api + "/api/auth/local/login",
            new APIRequestContextOptions { DataObject = new { username, password } }).ConfigureAwait(false);
        if (response.Status != 200)
            throw new InvalidOperationException($"Harness sign-in failed with HTTP {response.Status}.");
    }

    private async Task WaitForApiAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        var last = "no response";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(Origins.Api + "/api/auth/me").ConfigureAwait(false);
                if ((int)response.StatusCode == 401)
                    return;

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                last = $"HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 600)]}";
            }
            catch (Exception ex)
            {
                last = ex.GetType().Name + ": " + ex.Message;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Management.Host did not answer GET /api/auth/me with 401 within 90 s. Last result: {last}{Environment.NewLine}Host log tail:{Environment.NewLine}{Host.Logs.Dump()}");
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserHarnessCollection : ICollectionFixture<BrowserHarness>
{
    public const string Name = "Browser harness";
}
