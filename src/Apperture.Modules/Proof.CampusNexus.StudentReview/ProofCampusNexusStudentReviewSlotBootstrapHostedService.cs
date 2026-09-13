using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Registers proof module capability slots for integration bindings (Management host).
/// </summary>
public sealed class ProofCampusNexusStudentReviewSlotBootstrapHostedService : IHostedService
{
    private readonly IModuleSlotRegistry _slots;

    public ProofCampusNexusStudentReviewSlotBootstrapHostedService(IModuleSlotRegistry slots) => _slots = slots;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var mid = ProofCampusNexusStudentReviewConstants.ModuleId;
        _slots.RegisterSlot(new CapabilitySlotDeclaration(mid, ProofCampusNexusStudentReviewConstants.SlotCnsApi, typeof(ICampusNexusApiClient).FullName!));
        _slots.RegisterSlot(new CapabilitySlotDeclaration(mid, ProofCampusNexusStudentReviewConstants.SlotIntegrationDb, typeof(IDbConnection).FullName!));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
