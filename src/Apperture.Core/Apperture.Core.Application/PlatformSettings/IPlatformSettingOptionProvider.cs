using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Provides selectable options for a specific platform setting (e.g. connector definitions, catalogs).
/// Providers do not own the setting value; the backend must validate submitted selections against provider output.
/// </summary>
public interface IPlatformSettingOptionProvider
{
    string ProviderKey { get; }

    Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsAsync(
        PlatformSettingDefinition definition,
        CancellationToken cancellationToken = default);
}

