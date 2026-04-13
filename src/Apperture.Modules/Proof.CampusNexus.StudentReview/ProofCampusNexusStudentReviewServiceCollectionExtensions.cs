using Apperture.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Proof.CampusNexus.StudentReview.Persistence;

namespace Proof.CampusNexus.StudentReview;

public static class ProofCampusNexusStudentReviewServiceCollectionExtensions
{
    /// <summary>
    /// Registers Epic 74 proof module persistence and optional slot bootstrap (Management) or worker-only persistence.
    /// </summary>
    public static IServiceCollection AddProofCampusNexusStudentReviewIntegration(
        this IServiceCollection services,
        bool registerCapabilitySlotBootstrap = true,
        bool applyModulePersistenceMigrationsOnStartup = false)
    {
        services.AddProofStudentReviewPersistence(
            sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString,
            applyModulePersistenceMigrationsOnStartup);

        if (registerCapabilitySlotBootstrap)
            services.AddHostedService<ProofCampusNexusStudentReviewSlotBootstrapHostedService>();

        return services;
    }
}
