using System.Reflection;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// Design gate §18.1: GET endpoints stay safe (non-mutating with respect to Apperture state). Rule G only refuses an explicit
/// cross-site GET, so a same-site sibling can still trigger any GET; a GET that writes or calls out is therefore reviewed by name.
/// <para>
/// This freezes every GET action that can reach a service able to call an external system or change state. A new GET action on
/// such a controller, or a new such dependency on a controller with GET actions, fails here until it is classified in §18.1 and
/// listed below. Security-audit appends are observation and are permitted on GET, so the audit service is not in the set.
/// </para>
/// </summary>
public sealed class GetSafetyClassificationGuardTests
{
    private static readonly Type[] RemoteOrWritingServices =
    [
        typeof(IEntraDirectoryDiscoveryService),
        typeof(IEntraProvisioningDirectoryService),
        typeof(IEntraOpenIdConnectConfigurationValidator),
        typeof(PlatformSettingOptionProviderCoordinator),
        typeof(INotificationUserInboxService),
        typeof(IConnectionToolAccess),
        typeof(IConnectionManagementService),
        typeof(INotificationPipelineTestService),
    ];

    /// <summary>GET action → its §18.1 classification. Every entry was read and confirmed to make no Apperture state change.</summary>
    private static readonly IReadOnlyDictionary<string, string> Classified = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // G3: read-only Microsoft Graph search; annotation reads users and pending requests; no write.
        ["UsersEntraProvisioningController.SearchDirectory"] = "G3 remote read",
        // G4/G5: option providers read the connector catalog or list Campus Nexus campuses; no write.
        ["ConfigPlatformSettingsController.Options"] = "G4 remote read",
        ["IntegrationSettingsController.Options"] = "G5 remote read",
        // G9b: backfills the reader's missing inbox rows (new rows only); never changes read, unread or dismissed state.
        ["MeNotificationsController.List"] = "G9b idempotent read-model materialization",
        ["MeNotificationsController.UnreadCount"] = "G9b idempotent read-model materialization",
        // Local reads on controllers that also hold a remote-calling or writing service for their POST actions.
        ["ConfigEntraTenantsController.List"] = "local read",
        ["ConfigEntraTenantsController.GetById"] = "local read",
        ["ConfigPlatformSettingsController.List"] = "local read",
        ["ConfigPlatformSettingsController.Get"] = "local read",
        ["IntegrationSettingsController.List"] = "local read",
        ["IntegrationSettingsController.Get"] = "local read",
        ["ConnectionToolsController.Get"] = "local read (tool availability)",
        ["ConnectionsController.List"] = "local read",
        ["ConnectionsController.Get"] = "local read",
        ["ConnectionsController.GetOwnershipMovePreview"] = "local read (validation only)",
        ["MeNotificationsController.ToastDelta"] = "local read (bypasses the backfill)",
        ["NotificationDiagnosticsController.GetConfiguration"] = "local read",
        ["NotificationDiagnosticsController.GetHealth"] = "local read",
        ["NotificationDiagnosticsController.GetByIntegration"] = "local read",
        ["NotificationDiagnosticsController.GetByIncident"] = "local read",
        ["UsersEntraProvisioningController.ListRegistrations"] = "local read",
        ["UsersEntraProvisioningController.ListAccessRequests"] = "local read",
    };

    [Fact]
    public void Every_get_that_can_reach_a_remote_or_writing_service_is_classified()
    {
        var actual = GetActionsReachingRemoteOrWritingServices();

        var unclassified = actual.Where(name => !Classified.ContainsKey(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        unclassified.Should().BeEmpty(
            "a GET that can call out or write must be classified in design gate §18.1 before it ships (unclassified: {0})",
            string.Join(", ", unclassified));
        var stale = Classified.Keys.Where(name => !actual.Contains(name)).ToList();
        stale.Should().BeEmpty(
            "a classification whose GET no longer exists, or no longer reaches such a service, must be removed (stale: {0})",
            string.Join(", ", stale));
    }

    [Fact]
    public void The_entra_directory_searches_are_not_gets()
    {
        GetActionsReachingRemoteOrWritingServices().Should().NotContain(name =>
            name.StartsWith(nameof(ConfigEntraTenantsController) + ".", StringComparison.Ordinal)
            && name.Contains("Directory", StringComparison.Ordinal));
    }

    private static HashSet<string> GetActionsReachingRemoteOrWritingServices()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in typeof(EnrollController).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract))
        {
            var injected = controller.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToHashSet();
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!action.GetCustomAttributes<HttpMethodAttribute>().Any(a => a.HttpMethods.Contains("GET")))
                    continue;

                var reachable = injected.Concat(action.GetParameters()
                    .Where(p => p.GetCustomAttribute<FromServicesAttribute>() is not null)
                    .Select(p => p.ParameterType));
                if (reachable.Any(RemoteOrWritingServices.Contains))
                    result.Add(controller.Name + "." + action.Name);
            }
        }

        return result;
    }
}
