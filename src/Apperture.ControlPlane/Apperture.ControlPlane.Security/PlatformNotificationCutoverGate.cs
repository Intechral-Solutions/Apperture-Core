using Apperture.Core.Application.Notifications.Instances;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Epic 77 cutover guard: when <see cref="NotificationPipelineOptions.Enabled"/> is true,
/// Epic 69 <see cref="AttentionIncidentNotificationOrchestrator"/> is bypassed so only the platform pipeline delivers.
/// Default remains false (dual path) until operators enable cutover explicitly.
/// When Epic 69 runs, email uses the shared S8B/S8C composer and <c>Notifications:Email</c> SMTP settings (S8D).
/// </summary>
public static class PlatformNotificationCutoverGate
{
    public static bool ShouldInvokeEpic69Orchestrator(NotificationPipelineOptions options) =>
        !options.Enabled;
}
