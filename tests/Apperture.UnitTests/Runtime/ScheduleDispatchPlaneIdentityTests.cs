using Apperture.Core.Application.Services;
using Apperture.RuntimeHost.Windows.WorkerHost;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>
/// SWM-1: schedule plane must not inherit from APERTURE_RUNTIME_ID (host/claim identity).
/// </summary>
public sealed class ScheduleDispatchPlaneIdentityTests
{
    [Fact]
    public void ApplyPlaneDefaults_when_plane_unset_defaults_to_local()
    {
        var opts = new ScheduleDispatchOptions { RuntimePlaneId = null };

        ScheduleDispatchOptionsDefaults.ApplyPlaneDefaults(opts);

        opts.RuntimePlaneId.Should().Be("local");
        ScheduleDispatchOptionsDefaults.ResolveRuntimePlaneId(opts).Should().Be("local");
    }

    [Fact]
    public void ApplyPlaneDefaults_preserves_explicit_plane()
    {
        var opts = new ScheduleDispatchOptions { RuntimePlaneId = "worker-local" };

        ScheduleDispatchOptionsDefaults.ApplyPlaneDefaults(opts);

        opts.RuntimePlaneId.Should().Be("worker-local");
    }

    [Fact]
    public void PostConfigure_plane_local_when_APERTURE_RUNTIME_ID_is_worker_local()
    {
        // Mirrors WorkerHost Program.cs: bind ScheduleDispatch section + ApplyPlaneDefaults.
        // APERTURE_RUNTIME_ID remains host identity and must not become the dispatch plane.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APERTURE_RUNTIME_ID"] = "worker-local",
                ["RuntimeHostIdentity:RuntimeId"] = "worker-local"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.Configure<ScheduleDispatchOptions>(config.GetSection(ScheduleDispatchOptions.SectionName));
        services.PostConfigure<ScheduleDispatchOptions>(ScheduleDispatchOptionsDefaults.ApplyPlaneDefaults);
        services.Configure<RuntimeHostIdentityOptions>(opts =>
        {
            opts.RuntimeId =
                config["APERTURE_RUNTIME_ID"] ??
                config["RuntimeHostIdentity:RuntimeId"];
        });
        services.AddSingleton<ISchedulerHostIdentity>(sp =>
        {
            var scheduleOpts = sp.GetRequiredService<IOptions<ScheduleDispatchOptions>>().Value;
            var runtimeOpts = sp.GetRequiredService<IOptions<RuntimeHostIdentityOptions>>().Value;
            return new DefaultSchedulerHostIdentity(
                scheduleOpts.SchedulerHostId ?? runtimeOpts.RuntimeId ?? "worker");
        });

        var provider = services.BuildServiceProvider();
        var scheduleOpts = provider.GetRequiredService<IOptions<ScheduleDispatchOptions>>().Value;
        var runtimeOpts = provider.GetRequiredService<IOptions<RuntimeHostIdentityOptions>>().Value;
        var hostIdentity = provider.GetRequiredService<ISchedulerHostIdentity>();

        ScheduleDispatchOptionsDefaults.ResolveRuntimePlaneId(scheduleOpts).Should().Be("local");
        runtimeOpts.RuntimeId.Should().Be("worker-local");
        hostIdentity.HostId.Should().Be("worker-local");
    }

    [Fact]
    public void PostConfigure_explicit_ScheduleDispatch_RuntimePlaneId_wins()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APERTURE_RUNTIME_ID"] = "worker-local",
                ["ScheduleDispatch:RuntimePlaneId"] = "custom-plane"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<ScheduleDispatchOptions>(config.GetSection(ScheduleDispatchOptions.SectionName));
        services.PostConfigure<ScheduleDispatchOptions>(ScheduleDispatchOptionsDefaults.ApplyPlaneDefaults);

        var opts = services.BuildServiceProvider().GetRequiredService<IOptions<ScheduleDispatchOptions>>().Value;

        opts.RuntimePlaneId.Should().Be("custom-plane");
        ScheduleDispatchOptionsDefaults.ResolveRuntimePlaneId(opts).Should().Be("custom-plane");
    }
}
