using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Starts a run on an operator's request and records it (audit hardening design gate §13.1 W1, §22 AUD-1.3). The durable start
/// request, the run it creates and the audit row commit in one audited unit of work; the request row's <c>RequestedBy</c> is
/// the unit's own actor, so the two share it by construction and a caller cannot name someone else.
/// </summary>
/// <remarks>
/// <para>A start that changes nothing writes no audit row: a request whose idempotency key already named a start returns that
/// earlier run, and a definition that is not executable returns null (the refusal is recorded afterwards, as a W3 attempt by
/// the same actor). After an uncertain commit the unit verifies before anything else; a confirmed commit returns the run from
/// the committed row, and an unverifiable one is <see cref="AuditOutcomeUnknownException"/>.</para>
/// <para>Parameters appear in the row by name only. Scheduler-initiated starts do not come through here: they are the
/// scheduler's own and record <c>system:scheduler</c> on their start request.</para>
/// </remarks>
public interface IAuditedExecutionStartService
{
    /// <summary>Starts a workflow run. Null when the definition is not executable (nothing was created).</summary>
    ValueTask<WorkflowStartDispatchResult?> StartWorkflowAsync(
        StartWorkflowRequest request,
        ExecutionLaunchChannel launch,
        CancellationToken cancellationToken = default);

    /// <summary>Starts an operation run. Null when the definition is not executable (nothing was created).</summary>
    ValueTask<OperationStartDispatchResult?> StartOperationAsync(
        StartOperationRequest request,
        ExecutionLaunchChannel launch,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AuditedExecutionStartService : IAuditedExecutionStartService
{
    private readonly IAuditedUnitOfWork _unit;
    private readonly IAuditEventWriter _events;
    private readonly IWorkflowStartDispatchService _workflows;
    private readonly IOperationStartDispatchService _operations;
    private readonly IPrincipalContext _principal;
    private readonly AppertureDbContext _db;

    public AuditedExecutionStartService(
        IAuditedUnitOfWork unit,
        IAuditEventWriter events,
        IWorkflowStartDispatchService workflows,
        IOperationStartDispatchService operations,
        IPrincipalContext principal,
        AppertureDbContext db)
    {
        _unit = unit;
        _events = events;
        _workflows = workflows;
        _operations = operations;
        _principal = principal;
        _db = db;
    }

    public async ValueTask<WorkflowStartDispatchResult?> StartWorkflowAsync(
        StartWorkflowRequest request,
        ExecutionLaunchChannel launch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.WorkflowStartRequested, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Whatever the caller put in RequestedBy is replaced: the requester is the unit's own actor.
                var dispatched = await _workflows
                    .StartAsync(request with { RequestedBy = transaction.Actor.RecordedValue }, ct)
                    .ConfigureAwait(false);
                if (dispatched is null || dispatched.IsReplay)
                {
                    transaction.DeclareNoOp();
                    return dispatched;
                }

                await transaction.StageAsync(
                    AuditEvents.WorkflowStartRequested(
                        dispatched.RunId.Value,
                        dispatched.WorkflowInstanceId.Value,
                        dispatched.WorkflowDefinitionId.Value,
                        launch,
                        request.IntegrationId,
                        request.ProcedureId,
                        ParameterNames(request.Parameters),
                        dispatched.ResourceContextId,
                        request.CorrelationId),
                    ct).ConfigureAwait(false);
                return dispatched;
            },
            ReconstructWorkflowAsync,
            cancellationToken).ConfigureAwait(false);

        if (started is null)
        {
            await _events.TryWriteAsync(
                AuditEvents.StartBlocked(
                    isWorkflow: true,
                    request.WorkflowDefinitionId.Value,
                    launch,
                    request.IntegrationId,
                    request.ProcedureId,
                    request.CorrelationId),
                await AttemptActor.ResolveAsync(_principal, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        return started;
    }

    public async ValueTask<OperationStartDispatchResult?> StartOperationAsync(
        StartOperationRequest request,
        ExecutionLaunchChannel launch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.OperationStartRequested, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var dispatched = await _operations
                    .StartAsync(request with { RequestedBy = transaction.Actor.RecordedValue }, ct)
                    .ConfigureAwait(false);
                if (dispatched is null || dispatched.IsReplay)
                {
                    transaction.DeclareNoOp();
                    return dispatched;
                }

                await transaction.StageAsync(
                    AuditEvents.OperationStartRequested(
                        dispatched.RunId.Value,
                        dispatched.OperationInstanceId.Value,
                        dispatched.OperationDefinitionId.Value,
                        launch,
                        request.IntegrationId,
                        request.ProcedureId,
                        ParameterNames(request.Parameters),
                        dispatched.ResourceContextId,
                        request.CorrelationId),
                    ct).ConfigureAwait(false);
                return dispatched;
            },
            ReconstructOperationAsync,
            cancellationToken).ConfigureAwait(false);

        if (started is null)
        {
            await _events.TryWriteAsync(
                AuditEvents.StartBlocked(
                    isWorkflow: false,
                    request.OperationDefinitionId,
                    launch,
                    request.IntegrationId,
                    request.ProcedureId,
                    request.CorrelationId),
                await AttemptActor.ResolveAsync(_principal, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        return started;
    }

    /// <summary>The audit layer is given the parameter names only: a value can never reach it.</summary>
    private static IReadOnlyList<string>? ParameterNames(IReadOnlyDictionary<string, object?>? parameters) =>
        parameters is { Count: > 0 } ? parameters.Keys.ToList() : null;
    /// <summary>The run a confirmed commit created, from its primary row; current state where it can still be read.</summary>
    private async Task<WorkflowStartDispatchResult?> ReconstructWorkflowAsync(AuditCommittedOperation committed, CancellationToken cancellationToken)
    {
        var runId = committed.TargetId ?? throw new InvalidOperationException("The committed start names no run.");
        var run = await _db.OperationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken).ConfigureAwait(false);
        return new WorkflowStartDispatchResult(
            new OperationRunId(runId),
            new WorkflowInstanceId(run?.WorkflowInstanceId ?? committed.GetField("instanceId") ?? throw new InvalidOperationException("The committed start records no instance.")),
            new WorkflowDefinitionId(run?.DefinitionId ?? committed.GetField("definitionId") ?? throw new InvalidOperationException("The committed start records no definition.")),
            run?.Status ?? Apperture.Core.Domain.Workflow.WorkflowLifecycleStatusNames.Requested,
            IsReplay: false,
            ResourceContextId: run?.ResourceContextId);
    }

    private async Task<OperationStartDispatchResult?> ReconstructOperationAsync(AuditCommittedOperation committed, CancellationToken cancellationToken)
    {
        var runId = committed.TargetId ?? throw new InvalidOperationException("The committed start names no run.");
        var run = await _db.OperationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken).ConfigureAwait(false);
        return new OperationStartDispatchResult(
            new OperationRunId(runId),
            new OperationInstanceId(run?.WorkflowInstanceId ?? committed.GetField("instanceId") ?? throw new InvalidOperationException("The committed start records no instance.")),
            new Apperture.Core.Domain.Operations.OperationId(run?.DefinitionId ?? committed.GetField("definitionId") ?? throw new InvalidOperationException("The committed start records no definition.")),
            run?.Status ?? Apperture.Core.Domain.Workflow.WorkflowLifecycleStatusNames.Requested,
            IsReplay: false,
            ResourceContextId: run?.ResourceContextId);
    }
}

/// <summary>
/// The actor of an attempt, refusal or denial that is recorded after the fact: the authenticated principal, or
/// <see cref="AuditActor.Anonymous"/> when the request maps to no application session. There is no fallback literal such as
/// <c>system</c>: a person who was not identified is recorded as not signed in, never as the platform.
/// </summary>
public static class AttemptActor
{
    public static async ValueTask<AuditActor> ResolveAsync(IPrincipalContext principal, CancellationToken cancellationToken) =>
        await principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false) is null
            ? AuditActor.Anonymous
            : AuditActor.CurrentPrincipal;
}
