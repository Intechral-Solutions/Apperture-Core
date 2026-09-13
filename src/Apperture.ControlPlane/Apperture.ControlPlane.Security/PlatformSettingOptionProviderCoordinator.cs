using Apperture.Core.Application.PlatformSettings;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Builds provider context, enforces authorization, and invokes registered option providers.
/// </summary>
public sealed class PlatformSettingOptionProviderCoordinator
{
    private readonly IPlatformSettingOptionProviderRegistry _optionProviderRegistry;
    private readonly PlatformSettingOptionProviderAuthorization _authorization;
    private readonly IntegrationSettingOptionProviderAuthorization _integrationAuthorization;

    public PlatformSettingOptionProviderCoordinator(
        IPlatformSettingOptionProviderRegistry optionProviderRegistry,
        PlatformSettingOptionProviderAuthorization authorization,
        IntegrationSettingOptionProviderAuthorization integrationAuthorization)
    {
        _optionProviderRegistry = optionProviderRegistry;
        _authorization = authorization;
        _integrationAuthorization = integrationAuthorization;
    }

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsForReadAsync(
        PlatformSettingDefinition definition,
        string? integrationId,
        CancellationToken cancellationToken)
    {
        var actor = await _authorization.RequireReadAsync(
                ResolveIntegrationIdForAuthorization(definition, integrationId),
                cancellationToken)
            .ConfigureAwait(false);

        var context = BuildContext(definition, integrationId, actor);
        return await InvokeProviderAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsForValidationAsync(
        PlatformSettingDefinition definition,
        string? integrationId,
        CancellationToken cancellationToken)
    {
        var actor = await _authorization.RequireManageAsync(
                ResolveIntegrationIdForAuthorization(definition, integrationId),
                cancellationToken)
            .ConfigureAwait(false);

        var context = BuildContext(definition, integrationId, actor);
        return await InvokeProviderAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsForIntegrationSettingsReadAsync(
        PlatformSettingDefinition definition,
        string integrationId,
        CancellationToken cancellationToken)
    {
        var actor = await _integrationAuthorization.RequireReadAsync(integrationId, cancellationToken)
            .ConfigureAwait(false);

        var context = BuildContext(definition, integrationId, actor);
        return await InvokeProviderAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsForIntegrationSettingsValidationAsync(
        PlatformSettingDefinition definition,
        string integrationId,
        CancellationToken cancellationToken)
    {
        var actor = await _integrationAuthorization.RequireManageAsync(integrationId, cancellationToken)
            .ConfigureAwait(false);

        var context = BuildContext(definition, integrationId, actor);
        return await InvokeProviderAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }

    internal static PlatformSettingOptionProviderContext BuildContext(
        PlatformSettingDefinition definition,
        string? integrationId,
        IActor actor)
    {
        if (string.IsNullOrWhiteSpace(definition.OptionProviderKey))
            throw new ArgumentException($"Setting '{definition.Key}' is not provider-backed.");

        var normalizedIntegrationId = string.IsNullOrWhiteSpace(integrationId) ? null : integrationId.Trim();

        switch (definition.OptionProviderContextKind)
        {
            case PlatformSettingOptionProviderContextKind.Instance:
                if (normalizedIntegrationId is not null)
                {
                    throw new ArgumentException(
                        "IntegrationId must not be supplied for instance-scoped provider settings.");
                }

                return new PlatformSettingOptionProviderContext
                {
                    ContextKind = PlatformSettingOptionProviderContextKind.Instance,
                    SettingKey = definition.Key,
                    IntegrationId = null,
                    Actor = actor
                };

            case PlatformSettingOptionProviderContextKind.Integration:
                if (normalizedIntegrationId is null)
                {
                    throw new ArgumentException(
                        "IntegrationId is required for this provider-backed setting.");
                }

                return new PlatformSettingOptionProviderContext
                {
                    ContextKind = PlatformSettingOptionProviderContextKind.Integration,
                    SettingKey = definition.Key,
                    IntegrationId = normalizedIntegrationId,
                    Actor = actor
                };

            default:
                throw new InvalidOperationException(
                    $"Unsupported provider context kind '{definition.OptionProviderContextKind}'.");
        }
    }

    private static string? ResolveIntegrationIdForAuthorization(
        PlatformSettingDefinition definition,
        string? integrationId)
    {
        if (definition.OptionProviderContextKind == PlatformSettingOptionProviderContextKind.Integration)
            return string.IsNullOrWhiteSpace(integrationId) ? null : integrationId.Trim();

        return null;
    }

    private async Task<IReadOnlyList<PlatformSettingAllowedValue>> InvokeProviderAsync(
        PlatformSettingDefinition definition,
        PlatformSettingOptionProviderContext context,
        CancellationToken cancellationToken)
    {
        if (!_optionProviderRegistry.TryGet(definition.OptionProviderKey!, out var provider))
            throw new ArgumentException($"No option provider registered for '{definition.OptionProviderKey}'.");

        return await provider.GetOptionsAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }
}
