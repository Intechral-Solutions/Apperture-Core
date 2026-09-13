using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Registers Campus Nexus connection verification (<see cref="CampusNexusConnectionVerifier"/>) and its factory dependency.
/// </summary>
public sealed class CampusNexusConnectionVerifierContributor : IConnectionVerifierContributor
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddScoped<IConnectionVerifier, CampusNexusConnectionVerifier>();
    }
}
