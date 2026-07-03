using Apperture.Core.Application.Notifications.Instances;

namespace Apperture.ControlPlane.Security;

public static class PlatformNotificationCutoverGate
{
    public static bool ShouldInvokeEpic69Orchestrator(NotificationPipelineOptions options) =>
        Core.Application.Notifications.Cutover.PlatformNotificationCutoverGate.ShouldInvokeEpic69Orchestrator(options);
}
