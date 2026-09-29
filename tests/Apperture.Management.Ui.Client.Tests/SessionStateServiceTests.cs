using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-1A1: what the session bootstrap check makes of each <c>auth/me</c> outcome. Runs the real
/// <see cref="ManagementClient"/> against exact status codes. The rule: only a positive 401 means "signed out";
/// 403 is an unusable account; anything else (5xx, unexpected status, network failure, the access-request
/// session probe failing) is an availability problem and never a sign-in redirect (design gate D8).
/// </summary>
public sealed class SessionStateServiceTests
{
    private const string MeJson = """{"userId":"u1","displayName":"Ada","userPrincipal":"ada@example.test","roles":[],"roleNames":[],"identityLinks":[],"signInSource":"Local","localCredentialStatus":"Active","canChangeLocalPassword":true}""";

    private static (SessionStateService Session, StubHttpMessageHandler Http) Create()
    {
        var http = new StubHttpMessageHandler();
        var client = new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") });
        return (new SessionStateService(client), http);
    }

    [Fact]
    public async Task Refresh_200_is_authenticated()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Authenticated);
        session.User!.UserId.Should().Be("u1");
    }

    [Fact]
    public async Task Refresh_401_with_no_access_request_session_is_unauthenticated()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Unauthorized)
            .Respond("access-requests/session", HttpStatusCode.Unauthorized);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Unauthenticated);
    }

    [Fact]
    public async Task Refresh_401_with_an_access_request_session_is_the_access_request_state()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Unauthorized)
            .Respond("access-requests/session", HttpStatusCode.OK,
                """{"state":"NotRequested","displayName":"x","canRequest":true}""");

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.AccessRequestSession);
    }

    [Fact]
    public async Task Refresh_403_is_account_unavailable_not_a_shell()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Forbidden);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.AccountUnavailable);
        session.User.Should().BeNull();
        http.Count("access-requests/session").Should().Be(0, "a 403 is not a signed-out answer");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Refresh_5xx_or_unexpected_status_is_service_unavailable_never_signed_out(HttpStatusCode status)
    {
        var (session, http) = Create();
        http.Respond("auth/me", status);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.ServiceUnavailable);
        session.State.Should().NotBe(SessionLoadState.Unauthenticated);
        http.Count("access-requests/session").Should().Be(0, "an outage must not fall through to the access-request probe");
    }

    [Fact]
    public async Task Refresh_network_failure_is_service_unavailable()
    {
        var (session, http) = Create();
        http.Throw("auth/me", new HttpRequestException("connection refused"));

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.ServiceUnavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Refresh_401_then_access_request_probe_5xx_is_service_unavailable_not_signed_out(HttpStatusCode status)
    {
        // GetAccessRequestSessionAsync used to map 5xx to "no session", which read as signed out (finding F4).
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Unauthorized)
            .Respond("access-requests/session", status);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.ServiceUnavailable);
    }

    [Fact]
    public async Task Refresh_401_then_access_request_probe_network_failure_is_service_unavailable()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Unauthorized)
            .Throw("access-requests/session", new HttpRequestException("connection refused"));

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.ServiceUnavailable);
    }

    [Fact]
    public async Task Retrying_after_an_outage_recovers_to_authenticated()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.ServiceUnavailable);
        await session.RefreshAsync();
        session.State.Should().Be(SessionLoadState.ServiceUnavailable);

        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Authenticated);
    }

    // ---------- Soft refresh inside a signed-in tab (review finding: a second, uncoordinated sign-in path) ----------

    [Fact]
    public async Task Soft_refresh_401_in_a_signed_in_tab_is_a_suspicion_not_a_signed_out_verdict()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();
        var suspicions = 0;
        session.UnauthorizedDuringRefresh += () => suspicions++;
        var changed = 0;
        session.Changed += () => changed++;
        http.Respond("auth/me", HttpStatusCode.Unauthorized).Respond("access-requests/session", HttpStatusCode.Unauthorized);

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Authenticated, "recovery, not this refresh, decides that the session is gone");
        session.User.Should().NotBeNull();
        suspicions.Should().Be(1);
        changed.Should().Be(0);
        http.Count("access-requests/session").Should().Be(0, "a signed-in tab's suspicion goes to recovery, not to the access-request probe");
    }

    [Fact]
    public async Task Boot_refresh_401_still_means_unauthenticated_and_raises_no_suspicion()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.Unauthorized).Respond("access-requests/session", HttpStatusCode.Unauthorized);
        var suspicions = 0;
        session.UnauthorizedDuringRefresh += () => suspicions++;

        await session.RefreshAsync();

        session.State.Should().Be(SessionLoadState.Unauthenticated);
        suspicions.Should().Be(0);
    }

    [Fact]
    public async Task A_refresh_in_flight_never_overwrites_a_state_marked_meanwhile()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.RespondWhen("auth/me", gate, HttpStatusCode.OK, MeJson);

        var refresh = session.RefreshAsync();
        session.MarkSessionExpired();
        gate.SetResult();
        await refresh;

        session.State.Should().Be(SessionLoadState.SessionExpired, "recovery already owns the transition");
        session.User.Should().BeNull();
    }

    [Fact]
    public async Task MarkSessionExpired_clears_the_identity_and_notifies_once()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();
        var changed = 0;
        session.Changed += () => changed++;

        session.MarkSessionExpired();

        session.State.Should().Be(SessionLoadState.SessionExpired);
        session.User.Should().BeNull("a lost session's identity must not keep being treated as valid");
        changed.Should().Be(1);
    }

    [Fact]
    public async Task MarkAccountUnavailable_clears_the_identity()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();

        session.MarkAccountUnavailable();

        session.State.Should().Be(SessionLoadState.AccountUnavailable);
        session.User.Should().BeNull();
    }

    [Fact]
    public async Task MarkServiceUnavailable_clears_the_identity()
    {
        var (session, http) = Create();
        http.Respond("auth/me", HttpStatusCode.OK, MeJson);
        await session.RefreshAsync();

        session.MarkServiceUnavailable();

        session.State.Should().Be(SessionLoadState.ServiceUnavailable);
        session.User.Should().BeNull();
    }
}
