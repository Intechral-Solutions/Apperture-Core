using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class WorkerHostStartupDiTests
{
    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task ScheduleDispatchRunner_ConstructibleWithoutExplicitIntegrationRepositoryRegistration()
    {
        // This is the worker host regression guard:
        // WorkerHost must register IIntegrationRepository so ScheduleDispatchRunner can be constructed.
        var db = NewDb();

        var services = new ServiceCollection();
        services.AddSingleton(db);

        // Core schedule dispatch dependencies.
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

        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));

        // The critical part: do NOT register IIntegrationRepository here.
        // WorkerHost must do that via its helper.
        services.AddWorkerHostIntegrationRepository();

        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        runner.Should().NotBeNull();

        // Sanity: can resolve schedule repository from the same scope too.
        var scheduleRepo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        scheduleRepo.Should().NotBeNull();
    }
}

