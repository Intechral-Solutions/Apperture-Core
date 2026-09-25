using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A1: the per-tab session-recovery state machine. The probe (<c>auth/me</c>), the navigation manager, the
/// clock and the loop-breaker store are all faked; the session state is the REAL <see cref="SessionStateService"/>
/// so what the gate and the watcher would observe is what is asserted. No sleeps: time is a
/// <see cref="ManualTimeProvider"/>.
/// </summary>
public sealed class SessionRecoveryCoordinatorTests
{
    private const string Base = "http://localhost:9001/";
    private const string MeJson = """{"userId":"u1","displayName":"Ada","roles":[],"roleNames":[],"identityLinks":[],"signInSource":"Local","localCredentialStatus":"Active","canChangeLocalPassword":true}""";

    private sealed class Rig
    {
        public ManualTimeProvider Clock { get; } = new();
        public FakeProbe Probe { get; } = new();
        public InMemoryRecoveryHistory History { get; } = new();
        public TestNavigationManager Nav { get; }
        public StubHttpMessageHandler Http { get; } = new();
        public SessionStateService Session { get; }
        public SessionRecoveryCoordinator Coordinator { get; }

        public Rig(string uri = Base + "connections?status=active", SessionLoadState? state = SessionLoadState.Authenticated, InMemoryRecoveryHistory? sharedHistory = null)
        {
            Nav = new TestNavigationManager(Base, uri);
            var http = Http;
            switch (state)
            {
                case SessionLoadState.Authenticated:
                    http.Respond("auth/me", HttpStatusCode.OK, MeJson);
                    break;
                case SessionLoadState.Unauthenticated:
                    http.Respond("auth/me", HttpStatusCode.Unauthorized).Respond("access-requests/session", HttpStatusCode.Unauthorized);
                    break;
                case SessionLoadState.ServiceUnavailable:
                    http.Respond("auth/me", HttpStatusCode.ServiceUnavailable);
                    break;
                case SessionLoadState.AccountUnavailable:
                    http.Respond("auth/me", HttpStatusCode.Forbidden);
                    break;
            }

            Session = new SessionStateService(new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") }));
            History = sharedHistory ?? History;
            Coordinator = new SessionRecoveryCoordinator(Session, Probe, History, Nav, NullLogger<SessionRecoveryCoordinator>.Instance, Clock);
            if (state is not null && state != SessionLoadState.Unknown)
                Session.RefreshAsync().GetAwaiter().GetResult();
        }
    }

    // ---------- Decision matrix: auth/me after a 401 ----------

    [Fact]
    public async Task Probe_401_is_confirmed_loss_one_full_page_navigation_to_login_with_return_target_and_reason()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.FullPageLoads.Should().ContainSingle()
            .Which.Should().Be("/login?returnUrl=%2Fconnections%3Fstatus%3Dactive&reason=expired");
        rig.Nav.Navigations.Should().ContainSingle("recovery is one full-page navigation and nothing else");
        rig.Session.State.Should().Be(SessionLoadState.SessionExpired);
        rig.Session.User.Should().BeNull("stale identity must not keep being treated as valid");
        rig.Coordinator.Phase.Should().Be(SessionRecoveryPhase.Recovering);
        rig.Probe.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Probe_200_means_the_401_was_endpoint_specific_no_navigation_no_state_change()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Authenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.Navigations.Should().BeEmpty();
        rig.Session.State.Should().Be(SessionLoadState.Authenticated);
        rig.Session.User.Should().NotBeNull();
        rig.Coordinator.Phase.Should().Be(SessionRecoveryPhase.Normal);
    }

    [Fact]
    public async Task Probe_403_is_a_terminal_account_unavailable_state_not_a_login_redirect()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.AccountUnavailable);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.Navigations.Should().BeEmpty("a 403 identity failure must never loop through sign-in");
        rig.Session.State.Should().Be(SessionLoadState.AccountUnavailable);
        rig.Session.User.Should().BeNull();
    }

    [Fact]
    public async Task Probe_outage_is_service_unavailable_not_a_login_redirect()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.ServiceUnavailable);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.Navigations.Should().BeEmpty("an outage is not authentication loss");
        rig.Session.State.Should().Be(SessionLoadState.ServiceUnavailable);
        rig.Session.User.Should().BeNull();
    }

    [Fact]
    public async Task Probe_that_throws_is_treated_as_an_outage_and_never_escapes()
    {
        var rig = new Rig();
        rig.Probe.EnqueueFault(new InvalidOperationException("boom"));

        var act = () => rig.Coordinator.ReportUnauthenticatedAsync();

        await act.Should().NotThrowAsync();
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Session.State.Should().Be(SessionLoadState.ServiceUnavailable);
    }

    [Fact]
    public async Task Probe_that_hangs_times_out_as_an_outage()
    {
        var rig = new Rig();
        rig.Probe.EnqueueHang();

        var report = rig.Coordinator.ReportUnauthenticatedAsync();
        report.IsCompleted.Should().BeFalse();
        rig.Clock.Advance(SessionRecoveryCoordinator.ProbeTimeout + TimeSpan.FromSeconds(1));
        await report;

        rig.Nav.Navigations.Should().BeEmpty();
        rig.Session.State.Should().Be(SessionLoadState.ServiceUnavailable);
    }

    [Fact]
    public async Task A_soft_refresh_401_in_a_signed_in_tab_goes_through_the_same_single_recovery()
    {
        // e.g. Account > Security refreshes the session on open: auth/me answers 401 and the handler (which excludes
        // auth/me) never sees it. The session raises a suspicion and the coordinator owns the decision.
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);

        rig.Http.Respond("auth/me", HttpStatusCode.Unauthorized).Respond("access-requests/session", HttpStatusCode.Unauthorized);
        await rig.Session.RefreshAsync();
        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Probe.Calls.Should().Be(1, "the suspicion and a concurrent 401 coalesce into one probe");
        rig.Nav.FullPageLoads.Should().ContainSingle();
    }

    [Fact]
    public async Task A_throwing_session_subscriber_does_not_stop_the_navigation()
    {
        var rig = new Rig();
        rig.Session.Changed += () => throw new InvalidOperationException("a subscriber blew up");
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.FullPageLoads.Should().ContainSingle("the recovery edge is exception-safe");
    }

    [Fact]
    public async Task Stamps_dated_in_the_future_after_the_clock_moved_back_do_not_count()
    {
        var history = new InMemoryRecoveryHistory();
        var later = DateTimeOffset.Parse("2026-01-01T01:00:00Z");
        await history.SaveAsync([later, later, later]);
        var rig = new Rig(sharedHistory: history);   // its clock reads 2026-01-01T00:00:00Z: those stamps are in the future

        await RecoverAsync(rig);

        rig.Nav.FullPageLoads.Should().ContainSingle();
    }

    // ---------- Single-flight ----------

    [Fact]
    public async Task Simultaneous_reports_share_one_probe_and_one_navigation()
    {
        var rig = new Rig();
        var gate = rig.Probe.EnqueueGated();

        var reports = Enumerable.Range(0, 5).Select(_ => rig.Coordinator.ReportUnauthenticatedAsync()).ToArray();
        rig.Probe.Calls.Should().Be(1, "only one auth probe may be in flight");
        rig.Coordinator.Phase.Should().Be(SessionRecoveryPhase.Confirming);
        reports.Should().OnlyContain(t => !t.IsCompleted, "every reporter awaits the same decision");

        gate.SetResult(SessionProbeResult.Unauthenticated);
        await Task.WhenAll(reports);

        rig.Probe.Calls.Should().Be(1);
        rig.Nav.FullPageLoads.Should().ContainSingle("one recovery transition, no redirect storm");
    }

    [Fact]
    public async Task Reports_after_recovery_began_do_nothing_new()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);
        await rig.Coordinator.ReportUnauthenticatedAsync();

        await rig.Coordinator.ReportUnauthenticatedAsync();
        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Probe.Calls.Should().Be(1);
        rig.Nav.FullPageLoads.Should().ContainSingle();
    }

    [Fact]
    public async Task A_later_endpoint_specific_401_probes_again_but_a_probe_outcome_never_triggers_a_probe()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Authenticated);
        rig.Probe.Enqueue(SessionProbeResult.Authenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();
        rig.Probe.Calls.Should().Be(1, "no self-perpetuating probe loop: probing is driven only by reported 401s");

        await rig.Coordinator.ReportUnauthenticatedAsync();
        rig.Probe.Calls.Should().Be(2);
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Return_target_is_captured_at_the_recovery_edge_and_later_reports_cannot_overwrite_it()
    {
        var rig = new Rig();
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);
        await rig.Coordinator.ReportUnauthenticatedAsync();
        var first = rig.Nav.FullPageLoads.Single();

        // The document is unloading but the SPA may still move (an in-flight navigation); a later 401 must not re-capture.
        rig.Nav.NavigateTo("/runs");
        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.FullPageLoads.Should().ContainSingle().Which.Should().Be(first);
    }

    // ---------- Inertness ----------

    [Theory]
    [InlineData("login")]
    [InlineData("login/local?returnUrl=%2Fx")]
    [InlineData("account/forgot-password")]
    [InlineData("account/reset-password")]
    [InlineData("account/setup-password")]
    [InlineData("account/verify-email")]
    [InlineData("access-request")]
    public async Task Public_and_sign_in_routes_never_probe_or_navigate(string route)
    {
        var rig = new Rig(Base + route);
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Probe.Calls.Should().Be(0, "recovery must not recurse on the sign-in surfaces");
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SessionLoadState.Unauthenticated)]
    [InlineData(SessionLoadState.ServiceUnavailable)]
    [InlineData(SessionLoadState.AccountUnavailable)]
    [InlineData(SessionLoadState.Unknown)]
    public async Task Is_inert_unless_this_tab_believes_it_is_signed_in(SessionLoadState state)
    {
        var rig = new Rig(state: state);
        rig.Session.State.Should().Be(state);
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);

        await rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Probe.Calls.Should().Be(0);
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_navigate_when_the_operator_reached_a_public_route_while_the_probe_was_in_flight()
    {
        var rig = new Rig();
        var gate = rig.Probe.EnqueueGated();
        var report = rig.Coordinator.ReportUnauthenticatedAsync();

        rig.Nav.SetLocationWithoutNotifying("/login");
        gate.SetResult(SessionProbeResult.Unauthenticated);
        await report;

        rig.Nav.Navigations.Should().BeEmpty();
        rig.Session.State.Should().Be(SessionLoadState.Authenticated);
        rig.Coordinator.Phase.Should().Be(SessionRecoveryPhase.Normal);
    }

    // ---------- Loop breaker: at most 3 automatic recoveries per 60 s ----------

    private static async Task<Rig> RecoverAsync(Rig rig)
    {
        rig.Probe.Enqueue(SessionProbeResult.Unauthenticated);
        await rig.Coordinator.ReportUnauthenticatedAsync();
        return rig;
    }

    [Fact]
    public async Task Three_recoveries_in_a_minute_are_allowed_and_the_fourth_stops_auto_navigation()
    {
        var history = new InMemoryRecoveryHistory();
        var navigations = 0;
        SessionRecoveryCoordinator? last = null;
        Rig? lastRig = null;

        for (var i = 0; i < 4; i++)
        {
            // Each recovery is a fresh document (a new coordinator) sharing only the per-tab store.
            lastRig = new Rig(sharedHistory: history);
            lastRig.Clock.Advance(TimeSpan.FromSeconds(10 * i));
            last = lastRig.Coordinator;
            await RecoverAsync(lastRig);
            navigations += lastRig.Nav.FullPageLoads.Count;
        }

        navigations.Should().Be(3, "the fourth recovery inside 60 s must not navigate");
        last!.Phase.Should().Be(SessionRecoveryPhase.ManualSignInRequired);
        lastRig!.Session.State.Should().Be(SessionLoadState.SessionExpired);
        lastRig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_sign_in_navigates_even_when_the_breaker_is_open_and_keeps_the_captured_target()
    {
        var history = new InMemoryRecoveryHistory();
        for (var i = 0; i < 3; i++)
            await RecoverAsync(new Rig(sharedHistory: history));
        var rig = new Rig(Base + "runs?status=failed", sharedHistory: history);
        await RecoverAsync(rig);
        rig.Coordinator.Phase.Should().Be(SessionRecoveryPhase.ManualSignInRequired);
        rig.Nav.Navigations.Should().BeEmpty();

        rig.Coordinator.SignInNow();

        rig.Nav.FullPageLoads.Should().ContainSingle()
            .Which.Should().Be("/login?returnUrl=%2Fruns%3Fstatus%3Dfailed&reason=expired");
    }

    [Fact]
    public async Task Recoveries_outside_the_window_do_not_count_toward_the_breaker()
    {
        var history = new InMemoryRecoveryHistory();
        for (var i = 0; i < 3; i++)
            await RecoverAsync(new Rig(sharedHistory: history));

        // Every Rig's clock starts at the same instant, so a clock a window later sees the three stamps as stale.
        var later = new Rig(sharedHistory: history);
        later.Clock.Advance(SessionRecoveryCoordinator.RecoveryWindow + TimeSpan.FromSeconds(1));
        await RecoverAsync(later);

        later.Nav.FullPageLoads.Should().ContainSingle("old recoveries expire out of the 60 s window");
        later.Coordinator.Phase.Should().Be(SessionRecoveryPhase.Recovering);
    }

    [Fact]
    public async Task A_confirmed_endpoint_specific_401_does_not_count_toward_the_breaker()
    {
        var history = new InMemoryRecoveryHistory();
        for (var i = 0; i < 5; i++)
        {
            var r = new Rig(sharedHistory: history);
            r.Probe.Enqueue(SessionProbeResult.Authenticated);
            await r.Coordinator.ReportUnauthenticatedAsync();
        }

        history.Stamps.Should().BeEmpty("only real recoveries are recorded");
    }

    [Fact]
    public async Task A_failing_history_store_never_blocks_recovery()
    {
        var history = new InMemoryRecoveryHistory { FailAlways = true };
        var rig = new Rig(sharedHistory: history);

        await RecoverAsync(rig);

        rig.Nav.FullPageLoads.Should().ContainSingle();
    }
}

internal sealed class FakeProbe : ISessionProbe
{
    private readonly Queue<Func<CancellationToken, Task<SessionProbeResult>>> _script = new();

    public int Calls { get; private set; }

    public void Enqueue(SessionProbeResult result) => _script.Enqueue(_ => Task.FromResult(result));

    public void EnqueueFault(Exception ex) => _script.Enqueue(_ => throw ex);

    public void EnqueueHang() => _script.Enqueue(async ct =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return SessionProbeResult.Authenticated;
    });

    public TaskCompletionSource<SessionProbeResult> EnqueueGated()
    {
        var gate = new TaskCompletionSource<SessionProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _script.Enqueue(_ => gate.Task);
        return gate;
    }

    public Task<SessionProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (_script.Count == 0)
            throw new InvalidOperationException("Test bug: no probe outcome queued.");
        return _script.Dequeue()(cancellationToken);
    }
}

internal sealed class InMemoryRecoveryHistory : IRecoveryHistoryStore
{
    public List<DateTimeOffset> Stamps { get; private set; } = new();

    public bool FailAlways { get; init; }

    public Task<IReadOnlyList<DateTimeOffset>> LoadAsync()
    {
        if (FailAlways)
            throw new InvalidOperationException("storage unavailable");
        return Task.FromResult<IReadOnlyList<DateTimeOffset>>(Stamps.ToArray());
    }

    public Task SaveAsync(IReadOnlyList<DateTimeOffset> stamps)
    {
        if (FailAlways)
            throw new InvalidOperationException("storage unavailable");
        Stamps = stamps.ToList();
        return Task.CompletedTask;
    }
}
