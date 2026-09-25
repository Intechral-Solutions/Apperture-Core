using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// SEC-1A1 structural guards: session-loss recovery has ONE interception point (the HTTP pipeline handler) and
/// ONE coordinator. Nothing in the Management UI client may grow its own 401 handling, its own recovery, or a
/// different pipeline order. Cheap source scans, because these are exactly the regressions that would silently
/// reintroduce page-by-page recovery.
/// </summary>
public sealed class SessionRecoverySourceGuardTests
{
    private const string UiClientDir = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        return dir!.FullName;
    }

    private static IEnumerable<(string Relative, string Text)> UiClientSources() =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar)), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'), File.ReadAllText(f)));

    private static string[] Referencing(string token) =>
        UiClientSources().Where(s => s.Text.Contains(token, StringComparison.Ordinal)).Select(s => s.Relative).OrderBy(x => x).ToArray();

    [Fact]
    public void Only_the_pipeline_handler_reports_401_to_the_coordinator()
    {
        Referencing("ReportUnauthenticatedAsync(").Should().BeEquivalentTo(
        [
            $"{UiClientDir}/Services/AuthRecoveryHandler.cs",
            $"{UiClientDir}/Services/SessionRecoveryCoordinator.cs",
        ]);
    }

    [Fact]
    public void Only_the_coordinator_marks_the_session_expired()
    {
        Referencing("MarkSessionExpired(").Should().BeEquivalentTo(
        [
            $"{UiClientDir}/Services/SessionRecoveryCoordinator.cs",
            $"{UiClientDir}/Services/SessionStateService.cs",
        ]);
    }

    [Fact]
    public void No_page_or_feature_service_handles_a_401_itself()
    {
        // The probe and the pipeline handler are the only client-side places that decide anything from a 401. The one
        // other mention is the capabilities service declining to RETRY a 401 (REQ-5: a request that met 401 is never
        // re-sent); it neither recovers nor navigates.
        UiClientSources()
            .Where(s => System.Text.RegularExpressions.Regex.IsMatch(s.Text, @"\bUnauthorized\b"))
            .Select(s => s.Relative).OrderBy(x => x).Should().BeEquivalentTo(
        [
            $"{UiClientDir}/Services/AuthRecoveryHandler.cs",
            $"{UiClientDir}/Services/OperatorCapabilitiesService.cs",
            $"{UiClientDir}/Services/SessionProbe.cs",
        ]);
    }

    [Fact]
    public void Nothing_replays_a_request_from_the_recovery_path()
    {
        var recovery = string.Join('\n', new[] { "Services/AuthRecoveryHandler.cs", "Services/SessionRecoveryCoordinator.cs", "Services/SessionProbe.cs" }
            .Select(f => File.ReadAllText(Path.Combine(RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar), f.Replace('/', Path.DirectorySeparatorChar)))));

        // A replay would have to re-send the request the handler already saw. The handler's only inner call is the one send.
        System.Text.RegularExpressions.Regex.Matches(recovery, @"base\.SendAsync\(").Count.Should().Be(1, "the handler sends each request exactly once");
        recovery.Should().NotContain("CloneAsync").And.NotContain("Clone(request").And.NotContain("Polly").And.NotContain("Retry");
    }

    [Fact]
    public void The_http_pipeline_order_is_credentials_then_background_headers_then_recovery_then_the_network_handler()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar), "Program.cs"));

        var credentialsToHeaders = program.IndexOf("credentials.InnerHandler = headers;", StringComparison.Ordinal);
        var headersToRecovery = program.IndexOf("headers.InnerHandler = recovery;", StringComparison.Ordinal);
        var recoveryToNetwork = program.IndexOf("recovery.InnerHandler = new HttpClientHandler();", StringComparison.Ordinal);
        var client = program.IndexOf("new HttpClient(credentials)", StringComparison.Ordinal);

        credentialsToHeaders.Should().BeGreaterThan(0);
        headersToRecovery.Should().BeGreaterThan(credentialsToHeaders);
        recoveryToNetwork.Should().BeGreaterThan(headersToRecovery);
        client.Should().BeGreaterThan(recoveryToNetwork);
        program.Should().Contain("AddSessionRecovery(apiUri)");
        Regex_Count(program, "new HttpClient(").Should().Be(1, "one application pipeline; the probe client lives in the recovery registration");
    }

    [Fact]
    public void The_probe_client_has_no_recovery_handler_so_it_cannot_recurse()
    {
        var registration = File.ReadAllText(Path.Combine(
            RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar), "Services", "SessionRecoveryServiceCollectionExtensions.cs"));

        var start = registration.IndexOf("AddScoped<ISessionProbe>", StringComparison.Ordinal);
        var end = registration.IndexOf("AddScoped<IRecoveryHistoryStore", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var probeRegistration = registration[start..end];
        probeRegistration.Should().Contain("IncludeCredentialsHandler").And.NotContain("AuthRecoveryHandler");
    }

    [Fact]
    public void Public_route_and_sign_in_surfaces_are_excluded_from_recovery()
    {
        var handler = File.ReadAllText(Path.Combine(RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar), "Services", "AuthRecoveryHandler.cs"));
        var coordinator = File.ReadAllText(Path.Combine(RepoRoot(), UiClientDir.Replace('/', Path.DirectorySeparatorChar), "Services", "SessionRecoveryCoordinator.cs"));

        handler.Should().Contain("AuthRecoveryExclusions.IsExcluded");
        coordinator.Should().Contain("LoginRouteHelper.IsPublicPath");
        coordinator.Should().Contain("SessionLoadState.Authenticated", "recovery is inert unless this tab believes it is signed in");
    }

    private static int Regex_Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
