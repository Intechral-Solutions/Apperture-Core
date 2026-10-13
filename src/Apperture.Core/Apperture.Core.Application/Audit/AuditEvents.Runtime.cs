namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of runtime enrollment. The target is the runtime instance's scope key
// (ScopeKeyConstants.RuntimeInstance); the enrollment token is never recorded.
// AUD-1.4 slice 7: the typed producer of the runtime shutdown request, which has the same target.
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

    /// <summary>
    /// An operator queued a shutdown request for a runtime. The outcome is <c>requested</c>: the request is durable, and nothing
    /// says the runtime has stopped. The requester is the row's actor; a duplicate request returns the queued one and records
    /// nothing, so this row exists only for the request that created <paramref name="requestId"/>.
    /// </summary>
    public static AuditEvent RuntimeShutdownRequestCreated(string runtimeScopeKey, string runtimeId, long requestId) =>
        Event(
            AuditActionNames.RuntimeShutdownRequestCreated,
            Target(AuditTargetTypes.RuntimeInstance, runtimeScopeKey),
            AuditOutcomeCodes.Requested,
            ("runtimeId", runtimeId),
            ("requestId", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("controlKind", "shutdown"));
}
