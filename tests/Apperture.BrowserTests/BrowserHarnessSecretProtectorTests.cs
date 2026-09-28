using System.Security.Cryptography;
using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// Always-on (not gated by APPERTURE_BROWSER_TESTS): the harness secret protector round-trips, is
/// test-owned, and is the replacement LiveManagementHost installs over the production DPAPI default.
/// </summary>
public sealed class BrowserHarnessSecretProtectorTests
{
    [Fact]
    public async Task Protect_unprotect_round_trips_and_is_not_plaintext()
    {
        var protector = new BrowserHarnessSecretProtector();
        var plaintext = "harness-secret-material"u8.ToArray();

        var protectedBytes = await protector.ProtectAsync(plaintext, "dpapi");
        protectedBytes.Should().NotEqual(plaintext);
        protectedBytes.Length.Should().BeGreaterThan(plaintext.Length);

        var roundTrip = await protector.UnprotectAsync(protectedBytes, "dpapi");
        roundTrip.Should().Equal(plaintext);
    }

    [Fact]
    public async Task Unprotect_with_wrong_key_id_fails()
    {
        var protector = new BrowserHarnessSecretProtector();
        var protectedBytes = await protector.ProtectAsync("payload"u8.ToArray(), "key-a");

        var act = () => protector.UnprotectAsync(protectedBytes, "key-b").AsTask();
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public void Live_host_replaces_the_production_protector_with_the_harness_type()
    {
        var root = RepoRoot();
        var host = File.ReadAllText(Path.Combine(root, "tests", "Apperture.BrowserTests", "Infrastructure", "LiveManagementHost.cs"));
        host.Should().Contain("RemoveAll<ISecretProtector>()");
        host.Should().Contain("BrowserHarnessSecretProtector");
        host.Should().Contain("_secretProtector");
        host.Should().NotContain("DpapiSecretProtector");

        var program = File.ReadAllText(Path.Combine(root, "src", "Apperture.ControlPlane", "Apperture.Management.Host", "Program.cs"));
        program.Should().Contain("AddAppertureSqlSecrets();");
        program.Should().NotContain("BrowserHarnessSecretProtector");
        program.Should().NotContain("ISecretProtector");

        var secrets = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Apperture.Persistence",
            "Apperture.Persistence.SqlServer",
            "ServiceCollectionExtensions.Secrets.cs"));
        secrets.Should().Contain("AddHostedService<SecretProtectorStartupValidationHostedService>");
        host.Should().Contain("_buildingTestServerTwin");
        host.Should().Contain("RemoveAll<IHostedService>()", "only the unused TestServer twin strips hosted services; Kestrel keeps startup validation");
    }

    private static string RepoRoot() =>
        BrowserSqlConfiguration.FindRepoRoot() ?? throw new InvalidOperationException("Repository root (.git) not found.");
}
