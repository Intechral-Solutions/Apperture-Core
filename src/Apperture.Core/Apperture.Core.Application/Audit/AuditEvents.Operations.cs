using System.Text.RegularExpressions;

namespace Apperture.Core.Application.Audit;

/// <summary>How an operator started a run: through the deprecated direct start API, or by launching a module procedure.</summary>
public enum ExecutionLaunchChannel
{
    Direct,
    Procedure,
}

// AUD-1.3: the typed producers of operator actions on runs and of the recovery and scheduler operations. A request names the
// persisted run (or item) it concerns and never a parameter value: a start lists parameter names only (gate §12.3).
// Identifiers copied from a request are bounded by the column they were stored in; where a refusal row would otherwise carry
// the caller's own text, the producer leaves out what the schema cannot hold instead of failing the row.
public static partial class AuditEvents
{
    private const int AuditLogRunIdLength = 64;
    private const int AuditLogCorrelationIdLength = 64;
    private const int AuditLogResourceContextIdLength = 64;

    private static string LaunchName(ExecutionLaunchChannel launch) => launch == ExecutionLaunchChannel.Procedure ? "procedure" : "direct";

    /// <summary>The id when the audit schema and its related-reference columns can hold it; otherwise null.</summary>
    private static string? BoundedId(string? value, int max = AuditDetailSchema.MaxIdLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length > max ? null : value;

    private static AuditRelatedReferences RunRelated(string? runId, string? resourceContextId = null, string? correlationId = null) =>
        new(
            RunId: BoundedId(runId, AuditLogRunIdLength),
            ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength),
            CorrelationId: BoundedId(correlationId, AuditLogCorrelationIdLength));

    /// <summary>
    /// A conservative identifier: a parameter name that looks like one is listed. Anything else is counted but never listed, so
    /// a name cannot carry structure or free text into the row.
    /// </summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.\\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();

    /// <summary>
    /// Only the names ever reach the audit layer: the caller passes the parameter names, so a value cannot be recorded even by
    /// mistake. Every name is counted; those that look like identifiers are listed.
    /// </summary>
    private static (int Count, IReadOnlyList<string>? Names) DescribeParameters(IReadOnlyList<string>? parameterNames)
    {
        if (parameterNames is null || parameterNames.Count == 0)
            return (0, null);

        var names = parameterNames
            .Where(k => k.Length <= AuditDetailSchemas.MaxParameterNameLength && ParameterNamePattern().IsMatch(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Take(AuditDetailSchemas.MaxParameterNames)
            .ToList();
        return (parameterNames.Count, names.Count == 0 ? null : names);
    }

    private static AuditEvent Created(string action, AuditTarget? target, string outcome, AuditRelatedReferences related, params (string Name, object? Value)[] fields) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            target,
            outcome,
            fields.Where(f => f.Value is not null).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal),
            related);

    /// <summary>
    /// An operator asked for a workflow run to start and the durable start request committed. Parameters appear by name only.
    /// </summary>
    public static AuditEvent WorkflowStartRequested(
        string runId,
        string instanceId,
        string definitionId,
        ExecutionLaunchChannel launch,
        string? integrationId,
        string? procedureId,
        IReadOnlyList<string>? parameterNames,
        string? resourceContextId,
        string? correlationId)
    {
        var (count, names) = DescribeParameters(parameterNames);
        return Created(
            AuditActionNames.WorkflowStartRequested,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId, correlationId),
            ("runId", runId), ("instanceId", instanceId), ("definitionId", definitionId), ("integrationId", integrationId), ("procedureId", procedureId),
            ("launch", LaunchName(launch)), ("parameterCount", count), ("parameterNames", names));
    }

    /// <summary>An operator asked for an operation run to start and the durable start request committed. Parameters appear by name only.</summary>
    public static AuditEvent OperationStartRequested(
        string runId,
        string instanceId,
        string definitionId,
        ExecutionLaunchChannel launch,
        string? integrationId,
        string? procedureId,
        IReadOnlyList<string>? parameterNames,
        string? resourceContextId,
        string? correlationId)
    {
        var (count, names) = DescribeParameters(parameterNames);
        return Created(
            AuditActionNames.OperationStartRequested,
            Target(AuditTargetTypes.OperationRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId, correlationId),
            ("runId", runId), ("instanceId", instanceId), ("definitionId", definitionId), ("integrationId", integrationId), ("procedureId", procedureId),
            ("launch", LaunchName(launch)), ("parameterCount", count), ("parameterNames", names));
    }

    /// <summary>
    /// A start was refused because its definition is not executable (module disabled, unavailable or unknown). No run exists, so
    /// the row has no target; the definition id appears only when the schema can hold it.
    /// </summary>
    public static AuditEvent StartBlocked(
        bool isWorkflow,
        string? definitionId,
        ExecutionLaunchChannel launch,
        string? integrationId,
        string? procedureId,
        string? correlationId) =>
        Created(
            isWorkflow ? AuditActionNames.WorkflowStartBlocked : AuditActionNames.OperationStartBlocked,
            null,
            AuditOutcomeCodes.Blocked,
            RunRelated(null, null, correlationId),
            ("definitionId", BoundedId(definitionId)), ("integrationId", BoundedId(integrationId)), ("procedureId", BoundedId(procedureId)),
            ("launch", LaunchName(launch)), ("reason", "definition_not_executable"));

    /// <summary>An operator asked for a run to be cancelled and the durable cancel intent committed.</summary>
    public static AuditEvent RunCancelRequested(bool isWorkflow, string runId, string instanceId, string definitionId, string runStatus, string? resourceContextId) =>
        Created(
            isWorkflow ? AuditActionNames.WorkflowRunCancelRequested : AuditActionNames.OperationRunCancelRequested,
            Target(isWorkflow ? AuditTargetTypes.WorkflowRun : AuditTargetTypes.OperationRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("instanceId", instanceId), ("definitionId", definitionId), ("runStatus", runStatus));

    /// <summary>An operator asked for a workflow run to be parked and the durable control request committed.</summary>
    public static AuditEvent WorkflowRunParkRequested(string runId, string instanceId, string definitionId, string runStatus, string? resourceContextId) =>
        Created(
            AuditActionNames.WorkflowRunParkRequested,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("instanceId", instanceId), ("definitionId", definitionId), ("runStatus", runStatus));

    /// <summary>An operator asked for a parked workflow run to be resumed and the durable control request committed.</summary>
    public static AuditEvent WorkflowRunResumeRequested(string runId, string instanceId, string definitionId, string runStatus, string? resourceContextId) =>
        Created(
            AuditActionNames.WorkflowRunResumeRequested,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("instanceId", instanceId), ("definitionId", definitionId), ("runStatus", runStatus));

    /// <summary>
    /// An operator asked for a failed or cancelled workflow run to be retried and the retry's durable start request committed. The
    /// target is the run being retried; <paramref name="retryRunId"/> is the run created for the retry. Through blocked-work
    /// recovery the same fact is recorded under the recovery action.
    /// </summary>
    public static AuditEvent WorkflowRetryRequested(string runId, string retryRunId, string definitionId, string? resourceContextId, bool viaBlockedWorkRecovery) =>
        Created(
            viaBlockedWorkRecovery ? AuditActionNames.BlockedWorkRetryRequested : AuditActionNames.WorkflowRetryRequested,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("retryRunId", retryRunId), ("definitionId", definitionId));

    /// <summary>A workflow retry was refused because the run's definition is not executable. Nothing was created.</summary>
    public static AuditEvent WorkflowRetryBlocked(string runId, string definitionId, string? resourceContextId) =>
        Created(
            AuditActionNames.WorkflowRetryBlocked,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Blocked,
            RunRelated(runId, resourceContextId, runId),
            ("runId", runId), ("definitionId", definitionId), ("reason", "definition_not_executable"));

    /// <summary>
    /// An operator asked for one failed operation item to be retried and the retry attempt, with the child run it started,
    /// committed. The reason is the operator's own text, in its declared field. An item retried as part of a bulk retry names
    /// the batch, so the rows of one batch can be found together; each item's row is the record of its own request.
    /// </summary>
    public static AuditEvent OperationItemRetryRequested(
        string operationItemId,
        string runId,
        string operationGroupId,
        string attemptId,
        int attemptIndex,
        string childRunId,
        string? reason,
        string? resourceContextId,
        string? bulkRequestId = null) =>
        Created(
            AuditActionNames.OperationItemRetryRequested,
            Target(AuditTargetTypes.OperationItem, operationItemId),
            AuditOutcomeCodes.Requested,
            RunRelated(runId, resourceContextId),
            ("operationItemId", operationItemId), ("runId", runId), ("operationGroupId", operationGroupId), ("attemptId", attemptId),
            ("attemptIndex", attemptIndex), ("childRunId", childRunId), ("reason", reason), ("bulkRequestId", BoundedId(bulkRequestId)));

    /// <summary>
    /// An operator asked for one failed operation item to be retried and the attempt was recorded as blocked: no child run
    /// exists. The attempt row is a committed change, so this is recorded with it.
    /// </summary>
    public static AuditEvent OperationItemRetryBlocked(
        string operationItemId,
        string runId,
        string operationGroupId,
        string attemptId,
        int attemptIndex,
        string errorCode,
        string? reason,
        string? resourceContextId,
        string? bulkRequestId = null) =>
        Created(
            AuditActionNames.OperationItemRetryBlocked,
            Target(AuditTargetTypes.OperationItem, operationItemId),
            AuditOutcomeCodes.Blocked,
            RunRelated(runId, resourceContextId),
            ("operationItemId", operationItemId), ("runId", runId), ("operationGroupId", operationGroupId), ("attemptId", attemptId),
            ("attemptIndex", attemptIndex), ("errorCode", errorCode), ("reason", reason), ("bulkRequestId", BoundedId(bulkRequestId)));

    /// <summary>
    /// An operator acknowledged failed items of one operation group, and the acknowledgements committed. The target is the run;
    /// the items acknowledged by this request are listed (a bulk request is bounded, so the list is too). Items already
    /// acknowledged, not failed or not in the group changed nothing and are only counted. The reason is the operator's own
    /// text, in its declared field.
    /// </summary>
    public static AuditEvent OperationItemAttentionAcknowledged(
        string runId,
        string operationGroupId,
        string disposition,
        string? reason,
        int requestedCount,
        IReadOnlyList<string> acknowledgedItemIds,
        string? resourceContextId) =>
        Created(
            AuditActionNames.OperationItemAttentionAcknowledged,
            Target(AuditTargetTypes.OperationRun, runId),
            AuditOutcomeCodes.Succeeded,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("operationGroupId", operationGroupId), ("disposition", disposition), ("reason", reason),
            ("requestedCount", requestedCount), ("acknowledgedCount", acknowledgedItemIds.Count),
            ("skippedCount", requestedCount - acknowledgedItemIds.Count), ("operationItemIds", acknowledgedItemIds));

    /// <summary>
    /// An operator reopened attention on acknowledged items of one operation group, and the reopenings committed. The
    /// acknowledgements are kept as history. The target is the run; the items reopened by this request are listed.
    /// </summary>
    public static AuditEvent OperationItemAttentionReopened(
        string runId,
        string operationGroupId,
        int requestedCount,
        IReadOnlyList<string> reopenedItemIds,
        string? resourceContextId) =>
        Created(
            AuditActionNames.OperationItemAttentionReopened,
            Target(AuditTargetTypes.OperationRun, runId),
            AuditOutcomeCodes.Succeeded,
            RunRelated(runId, resourceContextId),
            ("runId", runId), ("operationGroupId", operationGroupId), ("requestedCount", requestedCount),
            ("reopenedCount", reopenedItemIds.Count), ("skippedCount", requestedCount - reopenedItemIds.Count),
            ("operationItemIds", reopenedItemIds));

    /// <summary>
    /// A blocked-work retry was refused before anything was requested: the work is not eligible now. The definition id is the
    /// blocked run's own definition.
    /// </summary>
    public static AuditEvent BlockedWorkRetryRefused(string runId, string? definitionId, string reason, string? resourceContextId) =>
        Created(
            AuditActionNames.BlockedWorkRetryRefused,
            Target(AuditTargetTypes.WorkflowRun, runId),
            AuditOutcomeCodes.Rejected,
            RunRelated(runId, resourceContextId, runId),
            ("runId", runId), ("definitionId", BoundedId(definitionId)), ("reason", reason));

    /// <summary>
    /// A caller without the recovery permission tried to retry blocked work. The run id is the caller's own text, so it is
    /// recorded only when it is a well-formed id; the run may not exist.
    /// </summary>
    public static AuditEvent RecoveryRetryDenied(string? runId) =>
        Created(
            AuditActionNames.RecoveryRetryDenied,
            null,
            AuditOutcomeCodes.Denied,
            RunRelated(runId),
            ("runId", BoundedId(runId)), ("reason", "permission_missing"));

    /// <summary>A caller without the recovery permission tried a manual scheduler operation (<c>run_cycle</c> or <c>recover_stale</c>).</summary>
    public static AuditEvent SchedulerOperationDenied(string operation) =>
        Created(
            AuditActionNames.SchedulerOperationDenied,
            null,
            AuditOutcomeCodes.Denied,
            new AuditRelatedReferences(),
            ("operation", operation), ("reason", "permission_missing"));

    /// <summary>
    /// An operator ran a manual scheduler operation. The outcome is what the operation reported: succeeded, or failed when it
    /// threw. The scheduler's own dispatches are recorded as the scheduler's, not here.
    /// </summary>
    public static AuditEvent SchedulerOperationRequested(string operation, string? runtimePlaneId, int? recoveredCount, bool failed) =>
        Created(
            AuditActionNames.SchedulerOperationRequested,
            null,
            failed ? AuditOutcomeCodes.Failed : AuditOutcomeCodes.Succeeded,
            new AuditRelatedReferences(),
            ("operation", operation), ("runtimePlaneId", BoundedId(runtimePlaneId)), ("recoveredCount", recoveredCount));
}
