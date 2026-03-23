using Microsoft.Extensions.DependencyInjection;

namespace Unisa.CampusNexus.PaymentPlans;

public static class UnisaCampusNexusPaymentPlansServiceCollectionExtensions
{
    /// <summary>
    /// Registers UNISA payment plan services. Set <paramref name="registerCapabilitySlotBootstrap"/> to <c>true</c>
    /// only where <see cref="Apperture.Core.Application.Services.IModuleSlotRegistry"/> is registered (control plane);
    /// runtime workers must pass <c>false</c>.
    /// </summary>
    public static IServiceCollection AddUnisaCampusNexusPaymentPlansIntegration(
        this IServiceCollection services,
        bool registerCapabilitySlotBootstrap = true)
    {
        if (registerCapabilitySlotBootstrap)
            services.AddHostedService<UnisaCampusNexusPaymentPlansSlotBootstrapHostedService>();

        services.AddScoped<IUnisaPaymentPlanManagementService, UnisaPaymentPlanManagementService>();
        services.AddScoped<UnisaPaymentPlanArrangementRepository>();
        return services;
    }
}
