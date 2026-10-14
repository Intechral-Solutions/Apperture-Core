using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 5: the typed producers of the connection lifecycle. The connection is the target and its owning resource context the
// related reference. Names are snapshots and the connector an id. A change is described by the names of the fields that differ,
// never by their values: no config value, secret value, secret reference or connection string has a field. The verification and
// browse rows record only what the verifier or the remote system answered, never the text it answered with.
public static partial class AuditEvents
{
    /// <summary>A connection was created. <paramref name="fieldsStored"/> counts the secret fields whose material was stored with it.</summary>
    public static AuditEvent ConnectionCreated(
        string connectionId,
        string name,
        string connectorId,
        int fieldsStored,
        string? resourceContextId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldsStored);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionCreated),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("connectorId", connectorId), ("fieldsStored", fieldsStored)),
            ConnectionRelated(resourceContextId));
    }

    /// <summary>
    /// A connection's name, config or secret fields changed. The row is <c>connection.secret_rotated</c> when any secret field
    /// changed (its material rotated or its reference added, removed or pointed elsewhere) and <c>connection.updated</c> otherwise.
    /// Only <c>connection.updated</c> lists field keys (the connector's declared names, never values); the Secrets-category rotation
    /// row counts them, and the keys of material that was stored are on the unit's <c>connection.secret.stored</c> rows. A rotation
    /// that also changed config, and a change that only pointed a field at another reference, therefore record counts alone for
    /// those fields. An update that changes nothing is not recorded.
    /// </summary>
    public static AuditEvent ConnectionChanged(
        string connectionId,
        string name,
        string connectorId,
        bool nameChanged,
        IReadOnlyList<string> configChanged,
        IReadOnlyList<string> rotated,
        string? resourceContextId)
    {
        if (!nameChanged && configChanged.Count == 0 && rotated.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(configChanged));
        if (rotated.Count > 0)
        {
            // A Secrets-category row: counts only. The field keys are on the connection.secret.stored rows of the same unit.
            return AuditEvent.Create(
                AuditActionCatalog.Default.Get(AuditActionNames.ConnectionSecretRotated),
                Target(AuditTargetTypes.Connection, connectionId),
                AuditOutcomeCodes.Succeeded,
                Fields(
                    ("connectorId", connectorId),
                    ("nameChanged", nameChanged ? true : null),
                    ("configFieldsChanged", configChanged.Count == 0 ? null : configChanged.Count),
                    ("fieldsRotated", rotated.Count)),
                ConnectionRelated(resourceContextId));
        }

        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionUpdated),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("connectorId", connectorId),
                ("nameChanged", nameChanged ? true : null),
                ("configChanged", configChanged.Count == 0 ? null : configChanged)),
            ConnectionRelated(resourceContextId));
    }

    /// <summary>A connection was retired. Written in the same transaction as the state change.</summary>
    public static AuditEvent ConnectionRetired(string connectionId, string name, string connectorId, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionRetired),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("connectorId", connectorId)),
            ConnectionRelated(resourceContextId));

    /// <summary>
    /// A connection's stored material was used against its remote system to verify it (basis <c>external_attempt</c>). The row
    /// states what the verifier answered: <c>succeeded</c> only for a success, <c>failed</c> for a failure answer or when the
    /// verifier produced no answer because it threw (<paramref name="status"/> null), and <c>unknown</c> when it answered that
    /// verification was unavailable or when the caller cancelled the probe before it answered
    /// (<paramref name="noAnswerBecauseCancelled"/>: the platform observed no answer at all). Nothing about the remote system
    /// beyond that status is recorded.
    /// </summary>
    public static AuditEvent ConnectionVerified(
        string connectionId,
        string name,
        string connectorId,
        ConnectionVerificationStatus? status,
        string? resourceContextId,
        bool noAnswerBecauseCancelled = false)
    {
        var (outcome, statusName) = status switch
        {
            null => (noAnswerBecauseCancelled ? AuditOutcomeCodes.Unknown : AuditOutcomeCodes.Failed, null),
            ConnectionVerificationStatus.Success => (AuditOutcomeCodes.Succeeded, "success"),
            ConnectionVerificationStatus.Failed => (AuditOutcomeCodes.Failed, "failed"),
            ConnectionVerificationStatus.Unavailable => (AuditOutcomeCodes.Unknown, "unavailable"),
            _ => throw new ArgumentException("A verification that never ran has no result to record.", nameof(status)),
        };
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionVerified),
            Target(AuditTargetTypes.Connection, connectionId),
            outcome,
            Fields(("name", name), ("connectorId", connectorId), ("verificationStatus", statusName)),
            ConnectionRelated(resourceContextId));
    }

    /// <summary>
    /// One remote structure request against a connection's system returned a result (required event, delivered with it). A remote
    /// refusal is an answer, so it is recorded here with its outcome; the names and content the remote system returned never are.
    /// </summary>
    public static AuditEvent ConnectionStructureBrowsed(
        string connectionId,
        string name,
        string connectorId,
        string operation,
        ConnectionToolOutcome result,
        int? resultCount,
        bool? truncated,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionStructureBrowsed),
            Target(AuditTargetTypes.Connection, connectionId),
            result == ConnectionToolOutcome.Succeeded ? AuditOutcomeCodes.Succeeded : AuditOutcomeCodes.Failed,
            Fields(
                ("name", name),
                ("connectorId", connectorId),
                ("tool", SqlStructureToolName),
                ("operation", operation),
                ("result", BrowseResultName(result)),
                ("resultCount", resultCount),
                ("truncated", truncated)),
            ConnectionRelated(resourceContextId));

    /// <summary>A structure request that threw or was cancelled after the remote request started (attempt; no result was delivered).</summary>
    public static AuditEvent ConnectionStructureBrowseFailed(
        string connectionId,
        string name,
        string connectorId,
        string operation,
        bool cancelled,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionStructureBrowseFailed),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Failed,
            Fields(
                ("name", name),
                ("connectorId", connectorId),
                ("tool", SqlStructureToolName),
                ("operation", operation),
                ("reason", cancelled ? "cancelled" : "failed")),
            ConnectionRelated(resourceContextId));

    /// <summary>
    /// A connection's material was refused because a secret field's reference does not name a secret that field owns (attempt;
    /// nothing was delivered). Only the field's key and the kind of operation are recorded: never the reference, the secret that
    /// was found, who owns it, or which of the refusing conditions applied.
    /// </summary>
    public static AuditEvent ConnectionSecretResolutionRefused(string connectionId, string fieldKey, string path, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionSecretResolutionRefused),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Rejected,
            Fields(("fieldKey", fieldKey), ("path", path)),
            ConnectionRelated(resourceContextId));

    /// <summary>
    /// A secret field's material was stored for a connection in the same transaction as the connection change: <c>created</c> as a
    /// new secret, or <c>replaced</c> as a new active version of the secret the field already named. Only the field's declared
    /// key is recorded, never the material, the store id or a version.
    /// </summary>
    public static AuditEvent ConnectionSecretStored(string connectionId, string fieldKey, bool replaced, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ConnectionSecretStored),
            Target(AuditTargetTypes.Connection, connectionId),
            AuditOutcomeCodes.Succeeded,
            Fields(("fieldKey", fieldKey), ("change", replaced ? "replaced" : "created")),
            ConnectionRelated(resourceContextId));

    private const string SqlStructureToolName = "sql-structure";

    private static string BrowseResultName(ConnectionToolOutcome result) => result switch
    {
        ConnectionToolOutcome.Succeeded => "succeeded",
        ConnectionToolOutcome.RemoteDenied => "remote_denied",
        ConnectionToolOutcome.RemoteUnavailable => "remote_unavailable",
        ConnectionToolOutcome.SecretsUnavailable => "secrets_unavailable",
        _ => "failed",
    };

    private static AuditRelatedReferences ConnectionRelated(string? resourceContextId) =>
        new(ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength));
}
