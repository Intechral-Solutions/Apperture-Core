using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Hosting;
using System.Data;

namespace Unisa.CampusNexus.PaymentPlans;

/// <summary>
/// Registers UNISA Campus Nexus payment plan capability slots for binding in the control plane.
/// </summary>
public sealed class UnisaCampusNexusPaymentPlansSlotBootstrapHostedService : IHostedService
{
    private readonly IModuleSlotRegistry _slots;

    public UnisaCampusNexusPaymentPlansSlotBootstrapHostedService(IModuleSlotRegistry slots) => _slots = slots;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        string mid = UnisaCampusNexusPaymentPlansConstants.ModuleId;
        _slots.RegisterSlot(new CapabilitySlotDeclaration(mid, UnisaCampusNexusPaymentPlansConstants.SlotCnsApi, typeof(ICampusNexusApiClient).FullName!));
        _slots.RegisterSlot(new CapabilitySlotDeclaration(mid, UnisaCampusNexusPaymentPlansConstants.SlotSftp, typeof(ISftpFileClient).FullName!));
        _slots.RegisterSlot(new CapabilitySlotDeclaration(mid, UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb, typeof(IDbConnection).FullName!));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
