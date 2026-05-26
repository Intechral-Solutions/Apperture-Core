using System.Globalization;
using System.Net.Http;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Same-integration option provider: active Campus Nexus campuses for the integration's <c>CnsApi</c> binding.
/// </summary>
public sealed class CampusNexusCampusesOptionProvider : IPlatformSettingOptionProvider
{
    public const string ProviderKeyConst = "campusnexus.campuses";

    /// <summary>Slot name shared by Campus Nexus integration modules (Proof, UNISA, etc.).</summary>
    public const string CampusNexusApiSlotName = "CnsApi";

    private readonly IIntegrationRepository _integrations;
    private readonly IRuntimeSlotResolver _slotResolver;

    public CampusNexusCampusesOptionProvider(
        IIntegrationRepository integrations,
        IRuntimeSlotResolver slotResolver)
    {
        _integrations = integrations;
        _slotResolver = slotResolver;
    }

    public string ProviderKey => ProviderKeyConst;

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsAsync(
        PlatformSettingDefinition definition,
        PlatformSettingOptionProviderContext context,
        CancellationToken cancellationToken = default)
    {
        _ = definition;

        if (context.ContextKind != PlatformSettingOptionProviderContextKind.Integration
            || string.IsNullOrWhiteSpace(context.IntegrationId))
        {
            throw new ArgumentException("Integration context is required for campusnexus.campuses.");
        }

        var integration = await _integrations
            .GetByIdAsync(new IntegrationId(context.IntegrationId), cancellationToken)
            .ConfigureAwait(false);
        if (integration is null)
            throw new KeyNotFoundException($"Integration {context.IntegrationId} not found.");

        if (integration.State != IntegrationState.Active)
        {
            throw new InvalidOperationException(
                $"Integration {context.IntegrationId} is not active.");
        }

        var client = await _slotResolver
            .ResolveSlotAsync<ICampusNexusApiClient>(
                integration.ModuleId,
                CampusNexusApiSlotName,
                context.IntegrationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (client is null)
        {
            throw new InvalidOperationException(
                $"Integration {context.IntegrationId} does not have an active Campus Nexus API (CnsApi) binding.");
        }

        try
        {
            using (client)
            {
                var campuses = await CampusNexusCampusQueries
                    .ListActiveCampusesAsync(client, cancellationToken)
                    .ConfigureAwait(false);

                return campuses
                    .Select(MapCampusOption)
                    .ToList();
            }
        }
        catch (CampusNexusApiException ex)
        {
            throw new InvalidOperationException($"Campus Nexus API error: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Campus Nexus HTTP request failed: {ex.Message}", ex);
        }
    }

    public static PlatformSettingAllowedValue MapCampusOption(CampusODataRow campus)
    {
        var value = campus.Id.ToString(CultureInfo.InvariantCulture);
        var label = !string.IsNullOrWhiteSpace(campus.Name)
            ? campus.Name.Trim()
            : !string.IsNullOrWhiteSpace(campus.Code)
                ? campus.Code.Trim()
                : $"Campus {value}";

        string? description = null;
        if (!string.IsNullOrWhiteSpace(campus.Code))
        {
            var code = campus.Code.Trim();
            if (!string.Equals(code, label, StringComparison.OrdinalIgnoreCase))
                description = code;
        }

        return new PlatformSettingAllowedValue
        {
            Value = value,
            Label = label,
            Description = description
        };
    }
}
