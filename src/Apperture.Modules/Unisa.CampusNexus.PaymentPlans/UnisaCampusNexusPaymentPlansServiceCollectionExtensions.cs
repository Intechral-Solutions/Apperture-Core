using Apperture.Connectors.CampusNexus;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Unisa.CampusNexus.PaymentPlans;

public static class UnisaCampusNexusPaymentPlansServiceCollectionExtensions
{
    /// <summary>
    /// Registers UNISA payment plan services. Set <paramref name="registerCapabilitySlotBootstrap"/> to <c>true</c>
    /// only where <see cref="IModuleSlotRegistry"/> is registered (control plane);
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
        services.AddSingleton<IClientDefinitionProvider, CampusNexusClientDefinitionProvider>();
        return services;
    }
}
