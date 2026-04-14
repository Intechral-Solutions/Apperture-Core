using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.Sftp;

/// <summary>
/// Registers SFTP connection verification (<see cref="SftpConnectionVerifier"/>) and its factory dependency.
/// </summary>
public sealed class SftpConnectionVerifierContributor : IConnectionVerifierContributor
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddScoped<SftpConnectorClientFactory>();
        services.AddScoped<IConnectionVerifier, SftpConnectionVerifier>();
    }
}
