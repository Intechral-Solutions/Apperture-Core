using System.Data;
using System.Linq;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.IntegrationTests.ManagementHost;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proof.CampusNexus.StudentReview.Persistence;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans.Persistence;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Epic 73 / Epic 74: SQL-backed evidence for platform + module migrations (including Proof StudentReview), connector factory registration,
/// and schedule-dispatch DI against a migrated database. Requires the same SQL setup as other integration tests.
/// </summary>
[Collection("SqlServerEpic73Seams")]
public sealed class Epic73SqlSeamsIntegrationTests
{
    private static string RequireConnectionString()
    {
        var cs = SqlServerTestConfiguration.GetConnectionString();
        if (cs is null)
        {
            throw new InvalidOperationException(
                "SQL Server not configured. Set MSSQL_SA_PASSWORD (e.g. in repo-root .env) and ensure SQL Server is reachable (see tests/Apperture.IntegrationTests/README-ManagementHostGovernance.md).");
        }

        return cs;
    }

    [Fact]
    public async Task Platform_Unisa_and_Proof_module_migrations_apply_with_separate_history_tables()
    {
        var connectionString = RequireConnectionString();
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var platformOptions = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options;

        await using (var platform = new AppertureDbContext(platformOptions))
        {
            await platform.Database.MigrateAsync();
            (await platform.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        var unisaOptions = new DbContextOptionsBuilder<UnisaPaymentPlansDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_UnisaPaymentPlans"))
            .Options;

        await using (var unisa = new UnisaPaymentPlansDbContext(unisaOptions))
        {
            await unisa.Database.MigrateAsync();
            (await unisa.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        var proofOptions = ProofStudentReviewDbContextFactory.CreateOptions(connectionString);

        await using (var proof = new ProofStudentReviewDbContext(proofOptions))
        {
            await proof.Database.MigrateAsync();
            (await proof.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Connector_contributors_register_platform_factories_end_to_end()
    {
        var connectionString = RequireConnectionString();
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddConnectorResolution();
        services.AddPlatformSqlConnector();
        services.AddAppertureCampusNexusConnector();
        services.AddAppertureSftpConnector();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle("AddConnectorResolution registers one IHostedService for factory contributors.");
        await hostedServices[0].StartAsync(CancellationToken.None);

        var registry = provider.GetRequiredService<IConnectorClientFactoryRegistry>();
        registry.GetFactory<IDbConnection>(WellKnownConnectorDefinitionIds.PlatformSql).Should().NotBeNull();
        registry.GetFactory<ICampusNexusApiClient>(WellKnownConnectorDefinitionIds.PlatformCampusNexus).Should().NotBeNull();
        registry.GetFactory<ISftpFileClient>(WellKnownConnectorDefinitionIds.PlatformSftp).Should().NotBeNull();
    }

    [Fact]
    public async Task Schedule_dispatch_runner_resolves_against_migrated_sql_database()
    {
        var connectionString = RequireConnectionString();
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var platformOptions = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options;

        var platform = new AppertureDbContext(platformOptions);
        await platform.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddSingleton(platform);

        try
        {
            services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
            services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
            services.AddScoped<IAuditService, SqlAuditService>();

            var modules = new IModule[] { new HelloWorldModule() };
            services.AddSingleton<IEnumerable<IModule>>(modules);
            services.AddSingleton<IWorkflowDefinitionResolver>(sp =>
                new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), null));
            services.AddSingleton<IOperationRegistryService>(sp =>
                new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), null));
            services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
            services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
            services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
            services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
            services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("epic73-sql-seams"));
            services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
            services.AddWorkerHostIntegrationRepository();
            services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

            await using (var provider = services.BuildServiceProvider())
            {
                await using var scope = provider.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
                runner.Should().NotBeNull();
            }
        }
        finally
        {
            await platform.DisposeAsync();
        }
    }
}
