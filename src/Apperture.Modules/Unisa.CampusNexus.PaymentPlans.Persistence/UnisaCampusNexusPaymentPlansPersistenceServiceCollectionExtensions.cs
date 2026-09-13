using Apperture.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Unisa.CampusNexus.PaymentPlans.Persistence;

namespace Unisa.CampusNexus.PaymentPlans;

public static class UnisaCampusNexusPaymentPlansPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence-backed adapters for the UNISA payment-plan module.
    /// Host-owned bootstrap concerns stay here instead of the integration module project.
    /// </summary>
    public static IServiceCollection AddUnisaCampusNexusPaymentPlansPersistenceAdapters(
        this IServiceCollection services,
        bool applyModulePersistenceMigrationsOnStartup = false)
    {
        services.AddUnisaPaymentPlansPersistence(
            sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString,
            applyModulePersistenceMigrationsOnStartup);
        services.AddScoped<Persistence.UnisaPaymentPlanArrangementRepository>();
        return services;
    }
}
