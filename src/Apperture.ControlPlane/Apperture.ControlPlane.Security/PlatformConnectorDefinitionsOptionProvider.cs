using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

public sealed class PlatformConnectorDefinitionsOptionProvider : IPlatformSettingOptionProvider
{
    public const string ProviderKeyConst = "platform.connectorDefinitions";

    private readonly IConnectorDefinitionCatalogReadService _connectorDefinitions;

    public PlatformConnectorDefinitionsOptionProvider(IConnectorDefinitionCatalogReadService connectorDefinitions)
    {
        _connectorDefinitions = connectorDefinitions;
    }

    public string ProviderKey => ProviderKeyConst;

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsAsync(
        PlatformSettingDefinition definition,
        PlatformSettingOptionProviderContext context,
        CancellationToken cancellationToken = default)
    {
        // Instance-scoped platform catalog; integration context is ignored for this provider.
        _ = context;

        var defs = await _connectorDefinitions.ListAsync(cancellationToken).ConfigureAwait(false);
        return defs
            .Select(d => new PlatformSettingAllowedValue
            {
                Value = d.Id.Value,
                Label = d.Name,
                Description = d.Description
            })
            .ToList();
    }
}

