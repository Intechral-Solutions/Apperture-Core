using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Integration.Configuration;
using Microsoft.Extensions.Hosting;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Registers connection requirement slots from SDK configuration contributors at startup.
/// </summary>
public sealed class IntegrationConfigurationSlotRegistrationHostedService : IHostedService
{
    private readonly IModuleSlotRegistry _slotRegistry;
    private readonly IIntegrationConfigurationRegistry _configurationRegistry;

    public IntegrationConfigurationSlotRegistrationHostedService(
        IModuleSlotRegistry slotRegistry,
        IIntegrationConfigurationRegistry configurationRegistry)
    {
        _slotRegistry = slotRegistry;
        _configurationRegistry = configurationRegistry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var moduleId in _configurationRegistry.GetRegisteredModuleIds())
        {
            if (!_configurationRegistry.TryGetModuleConfiguration(moduleId, out var configuration))
                continue;

            foreach (var slot in ConnectionRequirementSlotFactory.ToCapabilitySlots(moduleId, configuration.Connections))
                _slotRegistry.RegisterSlot(slot);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
