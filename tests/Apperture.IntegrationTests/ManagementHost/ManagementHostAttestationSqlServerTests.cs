using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.IntegrationTests.Audit;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// The real Management Host, composed by its own <c>Program</c>, against real SQL Server on both isolation modes: it records its
/// attestation at start with Local established and Microsoft sign-in not established, without having built the Entra or OpenID
/// Connect options; real traffic builds them and the next refresh attests the host's own reading; and a graceful stop closes the
/// window at once. This is the only test class that exercises the production wiring of the publisher (its registration, its
/// position among the hosted services and the build-record hook in the handler configurer); the publisher's logic is tested
/// without a host elsewhere. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ManagementHostAttestationSqlServerTests
{
    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private sealed class Factory(string connectionString) : WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalAuth:Enabled"] = "true",
                    ["LocalAuth:MinPasswordLength"] = "8",
                    [ManagementHostWebApplicationFactory.ManagementUiRedirectKey] = ManagementHostWebApplicationFactory.TrustedUiOrigin,
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISecretProtector>();
                services.AddSingleton<ISecretProtector>(new IntegrationTestSecretProtector());
                services.RemoveAll<IBootstrapConnectionProfile>();
                services.AddSingleton<IBootstrapConnectionProfile>(new FixedBootstrapConnectionProfile(connectionString));
            });
        }
    }

    private static async Task<HostConfigurationAttestationSnapshot> ReadAsync(AuditSqlScratch scratch)
    {
        await using var db = scratch.NewDb();
        return await new SqlHostConfigurationAttestationStore(db).ReadAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_host_attests_at_start_before_any_traffic_without_building_the_entra_options(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_HostAttest_", rcsi, "0");
        using var factory = new Factory(scratch.ConnectionString);

        // Starting the host (and nothing else) is what writes the row. No request has been made.
        var health = factory.Services.GetRequiredService<IEntraOptionsBuildHealth>();

        health.OpenIdConnectOptionsBuilt.Should().BeFalse("no request has reached the authentication middleware");
        var snapshot = await ReadAsync(scratch);
        var row = snapshot.Rows.Should().ContainSingle("the host recorded itself at start").Subject;
        row.StoppedUtc.Should().BeNull();
        row.Source.Should().Be(HostConfigurationAttestationSource.ManagementHost);
        AttestedAuthenticationPayload.TryParse(row.Payload, out var payload).Should().BeTrue();
        payload!.LocalNow.Should().BeTrue();
        payload.LocalRestart.Should().BeTrue();
        payload.EntraNow.Should().BeNull("the process starting does not initialise Microsoft sign-in");

        var assessment = HostConfigurationAttestationClassifier.Assess(snapshot, HostConfigurationAttestationTiming.FreshnessWindow);
        assessment.IsAttested.Should().BeTrue();
        assessment.Reading!.Running!.Handler.State.Should().Be(RunningHandlerState.Unavailable);
        scratch.ExecutionMarker.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Real_traffic_builds_the_options_and_the_next_refresh_attests_the_hosts_own_reading(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_HostAttest_", rcsi, "0");
        using var factory = new Factory(scratch.ConnectionString);
        var health = factory.Services.GetRequiredService<IEntraOptionsBuildHealth>();
        health.OpenIdConnectOptionsBuilt.Should().BeFalse();

        using var client = factory.CreateClient();
        (await client.GetAsync("/api/bootstrap/status")).EnsureSuccessStatusCode();

        health.OpenIdConnectOptionsBuilt.Should().BeTrue("the authentication middleware built the handler's options for the request");
        var publisher = factory.Services.GetServices<IHostedService>().OfType<HostConfigurationAttestationPublisher>().Single();
        (await publisher.PublishOnceAsync()).Should().BeTrue();

        var row = (await ReadAsync(scratch)).Rows.Single();
        AttestedAuthenticationPayload.TryParse(row.Payload, out var payload).Should().BeTrue();
        payload!.EntraNow.Should().NotBeNull("the options exist now and the host reports them");
        payload.EntraNow!.Endpoints.Enabled.Should().BeFalse("this host has no Entra configuration");
        payload.EntraNow.Handler.State.Should().Be(RunningHandlerState.Disabled);
        payload.LocalNow.Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(row.Payload, "\"clientSecret\"").Should().BeFalse("no secret value is a field of the document; only hasClientSecret is");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_graceful_stop_of_the_host_closes_the_window_at_once(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_HostAttest_", rcsi, "0");
        var factory = new Factory(scratch.ConnectionString);
        _ = factory.Services;
        HostConfigurationAttestationClassifier.Assess(await ReadAsync(scratch), HostConfigurationAttestationTiming.FreshnessWindow).IsAttested.Should().BeTrue();

        await factory.DisposeAsync();

        var snapshot = await ReadAsync(scratch);
        snapshot.Rows.Single().StoppedUtc.Should().NotBeNull("the host marked itself stopped on shutdown");
        HostConfigurationAttestationClassifier.Assess(snapshot, HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.Stopped);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_attestation_is_the_last_hosted_service_before_the_web_host_and_a_restart_replaces_the_row(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_HostAttest_", rcsi, "0");
        string firstProcess;
        using (var first = new Factory(scratch.ConnectionString))
        {
            var names = first.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
            names.Last().Should().Be("GenericWebHostService");
            names[^2].Should().Be("HostConfigurationAttestationPublisher");
            firstProcess = (await ReadAsync(scratch)).Rows.Single().ProcessInstanceId;
        }

        using var second = new Factory(scratch.ConnectionString);
        _ = second.Services;

        var row = (await ReadAsync(scratch)).Rows.Should().ContainSingle("the same host id: the new process replaces the old row").Subject;
        row.ProcessInstanceId.Should().NotBe(firstProcess);
        row.StoppedUtc.Should().BeNull("the claim of the new process clears the old process's stop mark");
    }
}
