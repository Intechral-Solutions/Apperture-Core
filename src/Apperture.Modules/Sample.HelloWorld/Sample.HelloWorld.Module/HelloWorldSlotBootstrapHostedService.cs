using System.Data;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Sample.HelloWorld;

/// <summary>
/// Registers Sample.HelloWorld capability slots for integration bindings (Management host).
/// </summary>
public sealed class HelloWorldSlotBootstrapHostedService : IHostedService
{
    public const string ModuleId = "Sample.HelloWorld";
    public const string SlotDb = "Db";

    private readonly IModuleSlotRegistry _slots;

    public HelloWorldSlotBootstrapHostedService(IModuleSlotRegistry slots) => _slots = slots;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _slots.RegisterSlot(new CapabilitySlotDeclaration(ModuleId, SlotDb, typeof(IDbConnection).FullName!));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
