using System.Data;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Sample;

public sealed class HelloWorldSlotBootstrapTests
{
    [Fact]
    public async Task Slot_bootstrap_registers_Db_slot_for_Sample_HelloWorld()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleSlotRegistry, InMemoryModuleSlotRegistry>();
        services.AddSampleHelloWorldIntegration();

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<HelloWorldSlotBootstrapHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);

        var slots = provider.GetRequiredService<IModuleSlotRegistry>().GetSlotsForModule("Sample.HelloWorld");
        slots.Should().ContainSingle(s =>
            s.SlotName == "Db" && s.ContractType == typeof(IDbConnection).FullName);
    }
}
