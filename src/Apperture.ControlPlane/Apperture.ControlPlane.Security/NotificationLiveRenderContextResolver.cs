using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationLiveRenderContextResolver : INotificationLiveRenderContextResolver
{
    private readonly IIntegrationRepository _integrations;
    private readonly string _managementHostBaseUrl;

    public NotificationLiveRenderContextResolver(IIntegrationRepository integrations, IConfiguration configuration)
    {
        _integrations = integrations;
        var raw = configuration["ShellUi:ManagementHostUrl"];
        _managementHostBaseUrl = string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim().TrimEnd('/');
    }

    public async ValueTask<NotificationLiveRenderHints> ResolveAsync(
        string? integrationId,
        CancellationToken cancellationToken = default)
    {
        string? displayName = null;
        if (!string.IsNullOrWhiteSpace(integrationId))
        {
            var integration = await _integrations
                .GetByIdAsync(new IntegrationId(integrationId), cancellationToken)
                .ConfigureAwait(false);
            displayName = integration?.DisplayName;
        }

        return new NotificationLiveRenderHints(displayName, _managementHostBaseUrl);
    }
}
