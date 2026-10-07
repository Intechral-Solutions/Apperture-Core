namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of runtime enrollment. The target is the runtime instance's scope key
// (ScopeKeyConstants.RuntimeInstance); the enrollment token is never recorded.
public static partial class AuditEvents
{
    /// <summary>An administrator created an enrollment invitation for a runtime.</summary>
    public static AuditEvent RuntimeEnrollmentInvitationCreated(string runtimeScopeKey, string runtimeId, string invitationId, int capabilityCount) =>
        Event(
            AuditActionNames.RuntimeEnrollmentInvitationCreated,
            Target(AuditTargetTypes.RuntimeInstance, runtimeScopeKey),
            AuditOutcomeCodes.Succeeded,
            ("runtimeId", runtimeId),
            ("invitationId", invitationId),
            ("capabilityCount", capabilityCount));

    /// <summary>A runtime used its enrollment invitation and registered its identity.</summary>
    public static AuditEvent RuntimeEnrollmentCompleted(string runtimeScopeKey, string runtimeId, string invitationId) =>
        Event(
            AuditActionNames.RuntimeEnrollmentCompleted,
            Target(AuditTargetTypes.RuntimeInstance, runtimeScopeKey),
            AuditOutcomeCodes.Succeeded,
            ("runtimeId", runtimeId),
            ("invitationId", invitationId));
}
