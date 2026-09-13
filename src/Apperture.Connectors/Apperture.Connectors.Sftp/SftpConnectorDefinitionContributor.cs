using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.Connectors.Sftp;

/// <summary>
/// Registers the platform SFTP connector definition (metadata for control plane and workers).
/// </summary>
public sealed class SftpConnectorDefinitionContributor : IConnectorDefinitionContributor
{
    public void Contribute(IConnectorDefinitionRegistry registry) =>
        registry.Register(WellKnownConnectorDefinitions.CreatePlatformSftp());
}
