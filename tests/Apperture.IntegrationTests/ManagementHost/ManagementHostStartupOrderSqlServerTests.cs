using System.Collections.Concurrent;
using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.IntegrationTests.Audit;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// WP4B-A, the startup contract against the real Management Host and real SQL Server, on both isolation modes: migrations run before
/// the secret store is validated, which runs before every governance startup step, and the web host starts last, as the framework
/// actually starts them (not as they are registered); nothing builds the Entra or OpenID Connect options before the first request; the
/// first request's build is complete on a new installation, an upgrade and a restart; and a build made before the migrations completed
/// is recorded as degraded, so that a host composed in another order reads Microsoft sign-in as unknown. Fails, never skips, without
/// SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ManagementHostStartupOrderSqlServerTests
{
    public enum Installation
    {
        /// <summary>No migration applied: the host creates the schema.</summary>
        New,

        /// <summary>The schema is one migration behind: the host applies it.</summary>
        Upgrade,

        /// <summary>The schema is current: a restart.</summary>
        Restart,
    }

    public static IEnumerable<object[]> Scenarios() =>
        from installation in Enum.GetValues<Installation>()
        from rcsi in new[] { false, true }
        select new object[] { installation, rcsi };

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static async Task<AuditSqlScratch> CatalogAsync(Installation installation, bool rcsi)
    {
        if (installation == Installation.Restart)
            return await AuditIsolation.CreateAsync("Apperture_StartupRestart_", rcsi);

        string migration;
        if (installation == Installation.New)
        {
            migration = "0";
        }
        else
        {
            await using var model = new AppertureDbContext(new DbContextOptionsBuilder<AppertureDbContext>().UseSqlServer("Server=unused").Options);
            var all = model.Database.GetMigrations().ToList();
            migration = all[^2];
        }

        return await AuditIsolation.CreateAtMigrationAsync("Apperture_Startup_", rcsi, migration);
    }

    // ---------------------------------------------------------------- the recording seams

    /// <summary>Notes every build of Entra options (the host records one whenever the framework builds the options).</summary>
    private sealed class RecordingHealth : IEntraOptionsBuildHealth
    {
        private readonly EntraOptionsBuildHealth _inner = new();

        public ConcurrentQueue<(string Options, EntraOptionsBuildQuality Quality, string? Reason)> Records { get; } = new();

        public void Record(object options, EntraOptionsBuildQuality quality, string? reasonCode = null)
        {
            Records.Enqueue((options.GetType().Name, quality, reasonCode));
            _inner.Record(options, quality, reasonCode);
        }

        public EntraOptionsBuildQuality? QualityOf(object options) => _inner.QualityOf(options);

        public string? ReasonOf(object options) => _inner.ReasonOf(options);

        public void Carry(object from, object to)
        {
            _inner.Carry(from, to);
        }
    }

    /// <summary>Notes the order in which the framework starts the hosted services.</summary>
    private sealed class StartRecorder
    {
        public ConcurrentQueue<string> Started { get; } = new();
    }

    private sealed class RecordingHostedService(string name, IHostedService inner, StartRecorder recorder) : IHostedService
    {
        public string Name { get; } = name;

        public IHostedService Inner { get; } = inner;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            recorder.Started.Enqueue(Name);
            await Inner.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Inner.StopAsync(cancellationToken);
    }

    /// <summary>A hosted service registered first that asks for the Entra options: the order the contract rules out.</summary>
    private sealed class EarlyOptionsBuilder(IServiceProvider services) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = services.GetRequiredService<IOptions<EntraAuthOptions>>().Value;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>The real Management Host over a scratch catalog, with the seams above and nothing else changed.</summary>
    private sealed class Factory(string connectionString, RecordingHealth health, StartRecorder? recorder = null, bool buildOptionsFirst = false)
        : WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>
    {
        public List<string> RegisteredHostedServices { get; } = [];

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
                services.RemoveAll<IEntraOptionsBuildHealth>();
                services.AddSingleton<IEntraOptionsBuildHealth>(health);

                RegisteredHostedServices.AddRange(
                    services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => (d.ImplementationType ?? d.ImplementationInstance?.GetType())?.Name ?? "factory"));

                if (recorder is not null)
                {
                    // Wrap each hosted service in place, so the recorded order is the order the framework starts them in.
                    for (var i = 0; i < services.Count; i++)
                    {
                        var descriptor = services[i];
                        if (descriptor.ServiceType != typeof(IHostedService) || descriptor.ImplementationType is not { } implementation)
                            continue;

                        // The web host is the framework's own and is left exactly as registered: replacing its descriptor with a factory
                        // makes the framework's registration check miss it and register a second web host.
                        if (implementation.Name == "GenericWebHostService")
                            continue;
                        services[i] = ServiceDescriptor.Singleton<IHostedService>(sp =>
                            new RecordingHostedService(implementation.Name, (IHostedService)ActivatorUtilities.CreateInstance(sp, implementation), recorder));
                    }
                }

                if (buildOptionsFirst)
                    services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp => new EarlyOptionsBuilder(sp)));
            });
        }
    }

    private static string NameOf(IHostedService service) => service is RecordingHostedService recorded ? recorded.Name : service.GetType().Name;

    // ================================================================================================ the order

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_real_host_starts_migrations_then_the_secret_store_then_every_governance_step_and_the_web_host_last(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_StartupOrder_", rcsi, "0");
        var recorder = new StartRecorder();
        using var factory = new Factory(scratch.ConnectionString, new RecordingHealth(), recorder);

        using var client = factory.CreateClient();

        // The order the framework starts them in (concurrent start is off, so it is the registration order of the host's services).
        factory.Services.GetRequiredService<IOptions<HostOptions>>().Value.ServicesStartConcurrently
            .Should().BeFalse("the framework starts hosted services one after another, in registration order");
        var started = recorder.Started.ToList();
        started.Should().NotBeEmpty();

        var migration = started.IndexOf("PersistenceMigrationHostedService");
        var secrets = started.IndexOf("SecretProtectorStartupValidationHostedService");
        migration.Should().BeGreaterThanOrEqualTo(0);
        secrets.Should().BeGreaterThan(migration, "the secret store is validated after the schema is current");
        var governance = new[]
        {
            "EmergencyAccessCanonicalizationHostedService",
            "ModulePermissionRegistrationHostedService",
            "PlatformAdminRoleRepairService",
            "BootstrapPlatformAdminAssignmentHostedService",
            "DevTestUserBootstrapHostedService",
        };
        foreach (var step in governance)
        {
            var at = started.IndexOf(step);
            at.Should().BeGreaterThan(secrets, $"{step} runs after the migrations and the secret validation");
        }

        // The web host: the framework registers it as a hosted service too. It is the last of the registered hosted services, so the
        // host listens only after every startup step above has finished.
        var effective = factory.Services.GetServices<IHostedService>().Select(NameOf).ToList();
        effective.Last().Should().Be("GenericWebHostService", "the web host service is the last hosted service the host starts");
        effective.Count(n => n == "GenericWebHostService").Should().Be(1, "one web host is registered");
        started.Should().Equal(effective.Take(effective.Count - 1), "the host started every other hosted service, in the order they are registered, before the web host");
        effective.IndexOf("PersistenceMigrationHostedService").Should().BeLessThan(effective.IndexOf("SecretProtectorStartupValidationHostedService"));
        foreach (var step in governance)
            effective.IndexOf("SecretProtectorStartupValidationHostedService").Should().BeLessThan(effective.IndexOf(step), step);
        factory.Services.GetRequiredService<MigrationCompletionState>().IsComplete.Should().BeTrue();
    }

    // ================================================================================================ no early build

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task No_entra_options_are_built_before_the_first_request_and_the_first_requests_build_is_complete(Installation installation, bool rcsi)
    {
        await using var scratch = await CatalogAsync(installation, rcsi);
        var health = new RecordingHealth();
        using var factory = new Factory(scratch.ConnectionString, health);

        using var client = factory.CreateClient();

        // The host has started: the migrations, the secret validation and every governance startup step have run.
        factory.Services.GetRequiredService<MigrationCompletionState>().IsComplete.Should().BeTrue();
        health.Records.Should().BeEmpty("no startup step may build the Entra or OpenID Connect options: the first build belongs to real traffic");

        using var response = await client.GetAsync("/api/bootstrap/status");

        health.Records.Should().NotBeEmpty("the first request's authentication builds the options");
        health.Records.Should().OnlyContain(r => r.Quality == EntraOptionsBuildQuality.Complete, "an empty registration table is a complete build, not a degraded one");
        health.Records.Should().NotContain(r => r.Reason != null);
        var reading = factory.Services.GetRequiredService<IDeploymentAuthenticationConfiguration>().Read();
        reading.Running.Should().NotBeNull();
        reading.Running!.EndpointsQuality.Should().Be(EntraOptionsBuildQuality.Complete);
    }

    // ================================================================================================ built before ready

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_composed_so_that_the_options_are_built_before_the_migrations_completed_records_a_degraded_build_and_reads_microsoft_sign_in_as_unknown(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAtMigrationAsync("Apperture_StartupEarly_", rcsi, "0");
        var health = new RecordingHealth();
        using var factory = new Factory(scratch.ConnectionString, health, buildOptionsFirst: true);

        using var client = factory.CreateClient();

        health.Records.Should().NotBeEmpty("the early step built the options");
        health.Records.Should().OnlyContain(
            r => r.Quality == EntraOptionsBuildQuality.Degraded && r.Reason == EntraOptionsBuildHealth.BuiltBeforeMigrationsComplete,
            "a build made before the migrations completed is never taken for a settled reading");
        factory.Services.GetRequiredService<MigrationCompletionState>().IsComplete.Should().BeTrue("the host went on to complete its migrations");
        var reading = factory.Services.GetRequiredService<IDeploymentAuthenticationConfiguration>().Read();
        reading.Running!.EndpointsQuality.Should().Be(EntraOptionsBuildQuality.Degraded, "the frozen options still say degraded after the host is ready");

        // The viability reading over it says it does not know, instead of describing the frozen options as settled.
        await using var db = scratch.NewDb();
        var configuration = await new EffectiveSignInConfigurationProvider(
                factory.Services.GetRequiredService<IDeploymentAuthenticationConfiguration>(), new SqlEntraInstanceRegistrationRepository(db))
            .ReadAsync();
        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
    }
}
