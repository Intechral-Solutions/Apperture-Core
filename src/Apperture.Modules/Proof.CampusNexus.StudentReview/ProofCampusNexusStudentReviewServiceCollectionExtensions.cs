using Microsoft.Extensions.DependencyInjection;

namespace Proof.CampusNexus.StudentReview;

public static class ProofCampusNexusStudentReviewServiceCollectionExtensions
{
    /// <summary>
    /// Registers Epic 74 proof module integration services. SQL persistence uses the <c>IntegrationDb</c> slot at runtime (no platform DbContext registration).
    /// </summary>
    public static IServiceCollection AddProofCampusNexusStudentReviewIntegration(
        this IServiceCollection services,
        bool registerCapabilitySlotBootstrap = true)
    {
        if (registerCapabilitySlotBootstrap)
            services.AddHostedService<ProofCampusNexusStudentReviewSlotBootstrapHostedService>();

        return services;
    }
}
