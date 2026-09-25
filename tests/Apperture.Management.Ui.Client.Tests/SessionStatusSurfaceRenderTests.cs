using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Organisms;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Auth;
using Apperture.Management.Ui.Shared.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A1: renders the REAL <c>SessionStatusSurface</c> for each unusable-session state. The copy and the
/// available actions are the operator-facing contract (design gate §11): account unavailable offers Sign out
/// and re-sign-in, service unavailable offers Retry, and neither offers a way to replay anything.
/// </summary>
public sealed class SessionStatusSurfaceRenderTests
{
    private static async Task<string> RenderAsync(SessionLoadState state, SessionRecoveryPhase? drivePhase = null)
    {
        var nav = new TestNavigationManager("http://localhost:9001/", "http://localhost:9001/connections");
        var session = new SessionStateService(new ManagementClient(new HttpClient(new StubHttpMessageHandler()) { BaseAddress = new Uri("https://api.example/api/") }));
        var probe = new FakeProbe();
        var history = new InMemoryRecoveryHistory();
        var coordinator = new SessionRecoveryCoordinator(session, probe, history, nav, NullLogger<SessionRecoveryCoordinator>.Instance);

        if (drivePhase == SessionRecoveryPhase.ManualSignInRequired)
        {
            // Open the loop breaker for real: three recoveries already on record inside the window.
            await history.SaveAsync([DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow]);
            var http = new StubHttpMessageHandler().Respond("auth/me", System.Net.HttpStatusCode.OK,
                """{"userId":"u1","displayName":"Ada","roles":[],"roleNames":[],"identityLinks":[],"signInSource":"Local","localCredentialStatus":"Active","canChangeLocalPassword":true}""");
            session = new SessionStateService(new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") }));
            await session.RefreshAsync();
            coordinator = new SessionRecoveryCoordinator(session, probe, history, nav, NullLogger<SessionRecoveryCoordinator>.Instance);
            probe.Enqueue(SessionProbeResult.Unauthenticated);
            await coordinator.ReportUnauthenticatedAsync();
            coordinator.Phase.Should().Be(SessionRecoveryPhase.ManualSignInRequired);
        }
        else if (drivePhase == SessionRecoveryPhase.Recovering)
        {
            var http = new StubHttpMessageHandler().Respond("auth/me", System.Net.HttpStatusCode.OK,
                """{"userId":"u1","displayName":"Ada","roles":[],"roleNames":[],"identityLinks":[],"signInSource":"Local","localCredentialStatus":"Active","canChangeLocalPassword":true}""");
            session = new SessionStateService(new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") }));
            await session.RefreshAsync();
            coordinator = new SessionRecoveryCoordinator(session, probe, history, nav, NullLogger<SessionRecoveryCoordinator>.Instance);
            probe.Enqueue(SessionProbeResult.Unauthenticated);
            await coordinator.ReportUnauthenticatedAsync();
            coordinator.Phase.Should().Be(SessionRecoveryPhase.Recovering);
        }

        var config = new ConfigurationBuilder().Build();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton(coordinator)
            .AddSingleton(new ShellHostNavigation(Options.Create(new ShellUiOptions { ManagementHostUrl = "http://localhost:9000" }), config))
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SessionStatusSurface>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(SessionStatusSurface.State)] = state }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Account_unavailable_explains_the_state_and_offers_sign_out_and_a_different_account_and_nothing_else()
    {
        var html = await RenderAsync(SessionLoadState.AccountUnavailable);

        html.Should().Contain("data-session-surface=\"account-unavailable\"");
        html.Should().Contain("Your account isn't available in Apperture.");
        html.Should().Contain(">Sign out<").And.Contain(">Sign in with a different account<");
        html.Should().NotContain(">Retry<");
    }

    [Fact]
    public async Task Service_unavailable_offers_retry_and_never_a_sign_in()
    {
        var html = await RenderAsync(SessionLoadState.ServiceUnavailable);

        html.Should().Contain("data-session-surface=\"service-unavailable\"");
        html.Should().Contain("Apperture can't reach the management service right now.");
        html.Should().Contain(">Retry<");
        html.Should().NotContain(">Sign in<").And.NotContain("Sign out");
    }

    [Fact]
    public async Task Session_expired_while_recovering_shows_calm_progress_with_a_fallback_sign_in_and_no_raw_error()
    {
        var html = await RenderAsync(SessionLoadState.SessionExpired, SessionRecoveryPhase.Recovering);

        html.Should().Contain("data-session-surface=\"session-expired\"");
        html.Should().Contain("Your session has expired.").And.Contain("Taking you to sign-in");
        html.Should().Contain(">Sign in<", "a tab restored from the back/forward cache must not be left with no way forward");
        html.Should().NotContainAny("401", "Unauthorized");
    }

    [Fact]
    public async Task Session_expired_with_the_loop_breaker_open_offers_a_manual_sign_in()
    {
        var html = await RenderAsync(SessionLoadState.SessionExpired, SessionRecoveryPhase.ManualSignInRequired);

        html.Should().Contain("data-session-surface=\"session-expired-manual\"");
        html.Should().Contain("Your session has expired.").And.Contain("Sign in to continue.");
        html.Should().Contain(">Sign in<");
    }
}
