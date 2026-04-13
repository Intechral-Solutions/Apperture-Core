using Apperture.Bootstrap;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using global::Unisa.CampusNexus.PaymentPlans.Persistence;

namespace Unisa.CampusNexus.PaymentPlans;

public static class UnisaCampusNexusPaymentPlansServiceCollectionExtensions
{
    /// <summary>
    /// Registers UNISA payment plan services. Set <paramref name="registerCapabilitySlotBootstrap"/> to <c>true</c>
    /// only where <see cref="IModuleSlotRegistry"/> is registered (control plane);
    /// runtime workers must pass <c>false</c>.
    /// </summary>
    /// <param name="applyModulePersistenceMigrationsOnStartup">When true, runs EF migrations for module-owned tables on startup (Management.Host).</param>
    public static IServiceCollection AddUnisaCampusNexusPaymentPlansIntegration(
        this IServiceCollection services,
        bool registerCapabilitySlotBootstrap = true,
        bool applyModulePersistenceMigrationsOnStartup = false)
    {
        services.AddUnisaPaymentPlansPersistence(
            sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString,
            applyModulePersistenceMigrationsOnStartup);

        if (registerCapabilitySlotBootstrap)
            services.AddHostedService<UnisaCampusNexusPaymentPlansSlotBootstrapHostedService>();

        services.AddScoped<IUnisaPaymentPlanManagementService, UnisaPaymentPlanManagementService>();
        services.AddScoped<UnisaPaymentPlanArrangementRepository>();
        return services;
    }
}
