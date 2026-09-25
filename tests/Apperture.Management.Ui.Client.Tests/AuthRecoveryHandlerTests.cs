using System.Net;
using System.Net.Http.Json;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A1: the single HTTP-pipeline interception point. It reports a 401 from a protected call to the
/// coordinator and returns the ORIGINAL response untouched. It never reports 403, never reports the
/// sign-in/bootstrap surfaces, and never re-sends anything.
/// </summary>
public sealed class AuthRecoveryHandlerTests
{
    private const string Base = "http://localhost:9001/";
    private const string MeJson = """{"userId":"u1","displayName":"Ada","roles":[],"roleNames":[],"identityLinks":[],"signInSource":"Local","localCredentialStatus":"Active","canChangeLocalPassword":true}""";

    private sealed class SpyCoordinator : ISessionRecoveryCoordinator
    {
        public int Reports { get; private set; }

        public TaskCompletionSource? Gate { get; set; }

        public Task ReportUnauthenticatedAsync()
        {
            Reports++;
            return Gate?.Task ?? Task.CompletedTask;
        }
    }

    private static (HttpClient Client, StubHttpMessageHandler Server, SpyCoordinator Spy) Pipeline()
    {
        var server = new StubHttpMessageHandler();
        var spy = new SpyCoordinator();
        var handler = new AuthRecoveryHandler(() => spy) { InnerHandler = server };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://api.example/api/") }, server, spy);
    }

    // ---------- 401 vs 403 ----------

    [Fact]
    public async Task A_401_from_a_protected_call_is_reported_once_and_the_original_response_is_returned()
    {
        var (client, server, spy) = Pipeline();
        server.Respond("connections", HttpStatusCode.Unauthorized, "original-body");

        using var response = await client.GetAsync("connections");

        spy.Reports.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Be("original-body", "the handler never alters or swallows the response");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Only_401_ever_reaches_the_coordinator_a_403_is_an_authorization_result(HttpStatusCode status)
    {
        var (client, server, spy) = Pipeline();
        server.Respond("connections", status);

        using var response = await client.GetAsync("connections");

        spy.Reports.Should().Be(0, "resource-level 403 (and every non-401) must never start session recovery");
        response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task A_network_failure_passes_through_untouched_and_is_not_reported()
    {
        var (client, server, spy) = Pipeline();
        server.Throw("connections", new HttpRequestException("connection refused"));

        var act = () => client.GetAsync("connections");

        await act.Should().ThrowAsync<HttpRequestException>();
        spy.Reports.Should().Be(0);
    }

    // ---------- Exclusion set: sign-in and bootstrap surfaces never report ----------

    [Theory]
    [InlineData("auth/me")]
    [InlineData("auth/local/login")]
    [InlineData("auth/local/mfa")]
    [InlineData("auth/local/logout")]
    [InlineData("auth/break-glass")]
    [InlineData("auth/sign-in-options")]
    [InlineData("auth/forgot-password")]
    [InlineData("auth/reset-password")]
    [InlineData("auth/setup-password")]
    [InlineData("auth/verify-contact-email")]
    [InlineData("access-requests")]
    [InlineData("access-requests/session")]
    [InlineData("access-requests/sign-out")]
    [InlineData("bootstrap/first-admin")]
    [InlineData("bootstrap/status")]
    public async Task A_401_from_a_sign_in_or_bootstrap_endpoint_never_starts_recovery(string path)
    {
        var (client, server, spy) = Pipeline();
        server.Respond(path, HttpStatusCode.Unauthorized);

        using var response = await client.SendAsync(new HttpRequestMessage(path.StartsWith("auth/local", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get, path));
        spy.Reports.Should().Be(0);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the caller's own handling (wrong password, etc.) still sees the 401");
    }

    [Fact]
    public async Task A_401_from_the_session_authenticated_change_password_endpoint_is_session_loss_and_reports()
    {
        var (client, server, spy) = Pipeline();
        server.Respond("auth/local/change-password", HttpStatusCode.Unauthorized);

        using var response = await client.PostAsync("auth/local/change-password", null);

        spy.Reports.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("connections", false)]
    [InlineData("control/capabilities", false)]
    [InlineData("me/notifications/toast-delta", false)]
    [InlineData("auth/me", true)]
    [InlineData("AUTH/ME", true)]
    [InlineData("auth/mex", false)]
    [InlineData("auth/me/anything", false)]
    [InlineData("auth/local/login", true)]
    [InlineData("auth/local/mfa", true)]
    [InlineData("auth/local/logout", true)]
    // Session-authenticated (class of [Authorize]): a 401 here IS session loss and must report.
    [InlineData("auth/local/change-password", false)]
    [InlineData("auth/local/other", false)]
    [InlineData("auth/localx", false)]
    [InlineData("access-requests/session", true)]
    // Same words, different route: the admin approval queue is a protected call and must report.
    [InlineData("users/entra/access-requests/abc/approve", false)]
    [InlineData("bootstrap/first-admin", true)]
    [InlineData("bootstrapping", false)]
    public void Exclusion_matches_exact_prefixes_only(string relativePath, bool excluded) =>
        AuthRecoveryExclusions.IsExcluded(new Uri("https://api.example/api/" + relativePath)).Should().Be(excluded);

    [Fact]
    public void Exclusion_handles_a_missing_uri_and_an_api_less_base()
    {
        AuthRecoveryExclusions.IsExcluded(null).Should().BeFalse();
        AuthRecoveryExclusions.IsExcluded(new Uri("https://api.example/auth/me")).Should().BeTrue();
    }

    // ---------- No replay ----------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task A_request_that_got_401_is_sent_exactly_once_never_replayed(string method)
    {
        var (client, server, _) = Pipeline();
        server.Respond("connections/c1", HttpStatusCode.Unauthorized);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "connections/c1")
        {
            Content = method == "GET" ? null : JsonContent.Create(new { name = "x" }),
        });

        server.Requests.Where(r => r.Path == "connections/c1").Should().ContainSingle("the server saw exactly one attempt");
    }

    [Fact]
    public async Task The_full_stack_never_replays_a_mutation_even_though_the_probe_confirms_loss()
    {
        // Real handler + real coordinator + real session, against a server whose session is gone.
        var server = new StubHttpMessageHandler()
            .Respond("auth/me", HttpStatusCode.OK, MeJson)
            .Respond("connections/c1/archive", HttpStatusCode.Unauthorized);
        var nav = new TestNavigationManager(Base, Base + "connections/c1");
        var probe = new FakeProbe();
        probe.Enqueue(SessionProbeResult.Unauthenticated);
        var (client, session) = Wire(server, nav, probe, out _);
        await session.RefreshAsync();

        using var response = await client.PostAsync("connections/c1/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        server.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
        nav.FullPageLoads.Should().ContainSingle().Which.Should().Contain("returnUrl=%2Fconnections%2Fc1").And.Contain("reason=expired");
    }

    // ---------- Awaiting the decision (no raw-401 flash) and cancellation ----------

    [Fact]
    public async Task The_caller_gets_its_response_only_after_the_recovery_decision()
    {
        var (client, server, spy) = Pipeline();
        spy.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond("connections", HttpStatusCode.Unauthorized);

        var call = client.GetAsync("connections");
        await Task.Delay(50);
        call.IsCompleted.Should().BeFalse("the page must not render its own raw 401 before the recovery decision");

        spy.Gate.SetResult();
        using var response = await call;
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cancelled_caller_stops_waiting_without_cancelling_the_shared_decision()
    {
        var (client, server, spy) = Pipeline();
        spy.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond("connections", HttpStatusCode.Unauthorized);
        using var cts = new CancellationTokenSource();

        var call = client.GetAsync("connections", cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        var act = async () => await call;
        await act.Should().ThrowAsync<OperationCanceledException>();
        spy.Gate.Task.IsCompleted.Should().BeFalse("the shared decision belongs to the coordinator, not to one caller");
    }

    // ---------- Whole-pipeline behaviors ----------

    [Fact]
    public async Task Simultaneous_401s_through_the_real_stack_produce_one_probe_and_one_navigation()
    {
        var server = new StubHttpMessageHandler().Respond("auth/me", HttpStatusCode.OK, MeJson);
        foreach (var p in new[] { "connections", "runs", "schedules", "control/capabilities", "menus" })
            server.Respond(p, HttpStatusCode.Unauthorized);
        var nav = new TestNavigationManager(Base, Base + "connections");
        var probe = new FakeProbe();
        var gate = probe.EnqueueGated();
        var (client, session) = Wire(server, nav, probe, out _);
        await session.RefreshAsync();

        var calls = new[] { "connections", "runs", "schedules", "control/capabilities", "menus" }
            .Select(p => client.GetAsync(p)).ToArray();
        await Task.Delay(50);
        gate.SetResult(SessionProbeResult.Unauthenticated);
        var responses = await Task.WhenAll(calls);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);
        probe.Calls.Should().Be(1);
        nav.FullPageLoads.Should().ContainSingle();
    }

    [Fact]
    public async Task Background_toast_poll_401_converges_on_one_recovery_and_the_watcher_goes_quiet()
    {
        // F10: the Toast watcher must not keep polling, backing off and failing after the session is gone.
        var server = new StubHttpMessageHandler()
            .Respond("auth/me", HttpStatusCode.OK, MeJson)
            .Respond("me/notifications/toast-delta", HttpStatusCode.Unauthorized);
        var nav = new TestNavigationManager(Base, Base + "connections");
        var probe = new FakeProbe();
        probe.Enqueue(SessionProbeResult.Unauthenticated);
        var clock = new ManualTimeProvider();
        var (client, session) = Wire(server, nav, probe, out _);
        using var toasts = new ToastService();
        using var watcher = new NotificationToastWatcher(new ManagementClient(client), toasts, session, clock);
        await session.RefreshAsync();

        watcher.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => nav.FullPageLoads.Count == 1);
        var pollsAtRecovery = server.Count("me/notifications/toast-delta");
        clock.Advance(TimeSpan.FromMinutes(20));
        await Task.Delay(100);

        pollsAtRecovery.Should().Be(1);
        server.Count("me/notifications/toast-delta").Should().Be(1, "the watcher stopped when the session was marked expired");
        clock.ActiveTimerCount.Should().Be(0, "no poll timer survives the recovery");
        toasts.Toasts.Should().BeEmpty("no error toast storm");
        probe.Calls.Should().Be(1);
        nav.FullPageLoads.Should().ContainSingle();
        session.State.Should().Be(SessionLoadState.SessionExpired);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
        condition().Should().BeTrue("the expected transition should have happened");
    }

    private static (HttpClient Client, SessionStateService Session) Wire(
        StubHttpMessageHandler server, TestNavigationManager nav, FakeProbe probe, out SessionRecoveryCoordinator coordinator)
    {
        SessionRecoveryCoordinator? c = null;
        var handler = new AuthRecoveryHandler(() => c!) { InnerHandler = server };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/api/") };
        var session = new SessionStateService(new ManagementClient(client));
        c = new SessionRecoveryCoordinator(
            session, probe, new InMemoryRecoveryHistory(), nav, NullLogger<SessionRecoveryCoordinator>.Instance, new ManualTimeProvider());
        coordinator = c;
        return (client, session);
    }
}
