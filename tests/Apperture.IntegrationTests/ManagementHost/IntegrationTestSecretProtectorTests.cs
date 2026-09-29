using System.Security.Cryptography;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// The IntegrationTests Management Host must supply its own protector. These tests do not start the host
/// or SQL. They pin the test-only AES-GCM replacement and keep it out of production and BrowserTests.
/// </summary>
public sealed class IntegrationTestSecretProtectorTests
{
    [Fact]
    public async Task Protect_unprotect_round_trips_and_is_not_plaintext()
    {
        var protector = new IntegrationTestSecretProtector();
        var plaintext = "integration-secret-material"u8.ToArray();

        var protectedBytes = await protector.ProtectAsync(plaintext, "sql-secret");
        protectedBytes.Should().NotEqual(plaintext);
        protectedBytes.Length.Should().BeGreaterThan(plaintext.Length);

        var roundTrip = await protector.UnprotectAsync(protectedBytes, "sql-secret");
        roundTrip.Should().Equal(plaintext);
    }

    [Fact]
    public async Task Unprotect_with_wrong_key_id_fails()
    {
        var protector = new IntegrationTestSecretProtector();
        var protectedBytes = await protector.ProtectAsync("payload"u8.ToArray(), "key-a");

        var act = () => protector.UnprotectAsync(protectedBytes, "key-b").AsTask();
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Replacement_after_production_registration_is_the_test_protector_and_passes_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureSqlSecrets();
        services.RemoveAll<ISecretProtector>();
        services.AddSingleton<ISecretProtector>(new IntegrationTestSecretProtector());
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISecretProtector>().Should().BeOfType<IntegrationTestSecretProtector>();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void Factory_replaces_the_production_protector_and_does_not_share_the_browser_harness()
    {
        var root = RepoRoot();
        var factory = File.ReadAllText(Path.Combine(
            root, "tests", "Apperture.IntegrationTests", "ManagementHost", "ManagementHostWebApplicationFactory.cs"));
        factory.Should().Contain("RemoveAll<ISecretProtector>()");
        factory.Should().Contain("IntegrationTestSecretProtector");
        factory.Should().NotContain("BrowserHarnessSecretProtector");
        factory.Should().NotContain("DpapiSecretProtector");

        var browserHost = File.ReadAllText(Path.Combine(
            root, "tests", "Apperture.BrowserTests", "Infrastructure", "LiveManagementHost.cs"));
        browserHost.Should().NotContain("IntegrationTestSecretProtector");

        var program = File.ReadAllText(Path.Combine(
            root, "src", "Apperture.ControlPlane", "Apperture.Management.Host", "Program.cs"));
        program.Should().Contain("AddAppertureSqlSecrets();");
        program.Should().NotContain("IntegrationTestSecretProtector");
        program.Should().NotContain("ISecretProtector");

        var secrets = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Apperture.Persistence",
            "Apperture.Persistence.SqlServer",
            "ServiceCollectionExtensions.Secrets.cs"));
        secrets.Should().Contain("AddHostedService<SecretProtectorStartupValidationHostedService>");
        secrets.Should().Contain("PlatformNotSupportedException");
        secrets.Should().NotContain("IntegrationTestSecretProtector");
        secrets.Should().NotContain("Testing");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Repository root (.git) not found.");
        return dir.FullName;
    }
}
