using Apperture.Core.Application;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Application.Validation;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Procedures;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class IntegrationProcedureService : IIntegrationProcedureService
{
    private readonly IIntegrationManagementService _integrations;
    private readonly IIntegrationReadinessService _readiness;
    private readonly IProcedureRegistryService _procedures;
    private readonly IWorkflowDefinitionResolver _workflowDefinitions;
    private readonly IOperationRegistryService _operations;
    private readonly IPrincipalContext _principalContext;
    private readonly IExecutablePermissionEvaluator _permissionEvaluator;
    private readonly IWorkflowStartAuthorizationService _workflowStartAuth;
    private readonly IOperationStartAuthorizationService _operationStartAuth;
    private readonly IWorkflowStartDispatchService _workflowDispatch;
    private readonly IOperationStartDispatchService _operationDispatch;

    public IntegrationProcedureService(
        IIntegrationManagementService integrations,
        IIntegrationReadinessService readiness,
        IProcedureRegistryService procedures,
        IWorkflowDefinitionResolver workflowDefinitions,
        IOperationRegistryService operations,
        IPrincipalContext principalContext,
        IExecutablePermissionEvaluator permissionEvaluator,
        IWorkflowStartAuthorizationService workflowStartAuth,
        IOperationStartAuthorizationService operationStartAuth,
        IWorkflowStartDispatchService workflowDispatch,
        IOperationStartDispatchService operationDispatch)
    {
        _integrations = integrations;
        _readiness = readiness;
        _procedures = procedures;
        _workflowDefinitions = workflowDefinitions;
        _operations = operations;
        _principalContext = principalContext;
        _permissionEvaluator = permissionEvaluator;
        _workflowStartAuth = workflowStartAuth;
        _operationStartAuth = operationStartAuth;
        _workflowDispatch = workflowDispatch;
        _operationDispatch = operationDispatch;
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationProcedureListResponse?> ListAvailableAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        var id = new IntegrationId(integrationId);
        var integration = await _integrations.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var readiness = await _readiness.GetReadinessAsync(id, cancellationToken);
        if (readiness is null)
            return null;

        var integrationBlockedReason = ResolveIntegrationBlockedReason(readiness);
        var moduleProcedures = await _procedures.GetByModuleAsync(integration.ModuleId, cancellationToken);
        var list = new List<IntegrationProcedureSummaryDto>();
        foreach (var p in moduleProcedures.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var procedureReadiness = await _readiness.GetProcedureReadinessAsync(id, p.Id, cancellationToken);
            var targetOk = await IsTargetExecutableAsync(p, cancellationToken).ConfigureAwait(false);
            var permOk = await HasProcedurePermissionsAsync(p, cancellationToken).ConfigureAwait(false);
            var executionStartOk = !targetOk || await HasExecutionStartPermissionAsync(p, cancellationToken).ConfigureAwait(false);
            var procedureBlockedReason = procedureReadiness is null
                ? integrationBlockedReason
                : ResolveIntegrationBlockedReason(procedureReadiness);
            var blocked = BuildBlockedCodes(procedureBlockedReason, targetOk, permOk, executionStartOk, p.LaunchKind);
            list.Add(ToSummary(p, blocked.Count == 0, blocked));
        }

        return new IntegrationProcedureListResponse(readiness.IsRunnable, list);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationProcedureDetailDto?> GetProcedureAsync(string integrationId, string procedureId, CancellationToken cancellationToken = default)
    {
        var id = new IntegrationId(integrationId);
        var integration = await _integrations.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var readiness = await _readiness.GetProcedureReadinessAsync(id, procedureId, cancellationToken);
        if (readiness is null)
            return null;

        var procedure = await _procedures.GetProcedureAsync(procedureId, cancellationToken);
        if (procedure is null || !string.Equals(procedure.ModuleId, integration.ModuleId, StringComparison.Ordinal))
            return null;

        var integrationBlockedReason = ResolveIntegrationBlockedReason(readiness);
        var targetOk = await IsTargetExecutableAsync(procedure, cancellationToken).ConfigureAwait(false);
        var permOk = await HasProcedurePermissionsAsync(procedure, cancellationToken).ConfigureAwait(false);
        var executionStartOk = !targetOk || await HasExecutionStartPermissionAsync(procedure, cancellationToken).ConfigureAwait(false);
        var blocked = BuildBlockedCodes(integrationBlockedReason, targetOk, permOk, executionStartOk, procedure.LaunchKind);
        var fields = ParseParameterFields(procedure.ParameterSchema);
        return new IntegrationProcedureDetailDto(
            procedure.Id,
            procedure.DisplayName,
            procedure.Description,
            procedure.Category,
            LaunchKindToken(procedure.LaunchKind),
            procedure.TargetDefinitionId,
            blocked.Count == 0,
            blocked,
            fields);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationProcedureLaunchOutcome> LaunchAsync(
        string integrationId,
        string procedureId,
        IntegrationProcedureLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = new IntegrationId(integrationId);
        var integration = await _integrations.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.IntegrationNotFound,
                $"Integration '{integrationId}' was not found.");

        var readiness = await _readiness.GetProcedureReadinessAsync(id, procedureId, cancellationToken);
        if (readiness is null)
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.IntegrationReadinessUnavailable,
                "Integration readiness could not be resolved.");

        var integrationBlockedReason = ResolveIntegrationBlockedReason(readiness);
        if (string.Equals(integrationBlockedReason, ProcedureBlockedReasonCodes.IntegrationInactive, StringComparison.Ordinal))
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.IntegrationInactive,
                "Integration is inactive; activate it before launching a procedure.");
        if (string.Equals(integrationBlockedReason, ProcedureBlockedReasonCodes.IntegrationHardBlocked, StringComparison.Ordinal))
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.IntegrationHardBlocked,
                "Integration has blocking readiness issues; resolve blockers before launching a procedure.");

        var procedure = await _procedures.GetProcedureAsync(procedureId, cancellationToken);
        if (procedure is null)
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.ProcedureNotFound,
                $"Procedure '{procedureId}' was not found.");

        if (!string.Equals(procedure.ModuleId, integration.ModuleId, StringComparison.Ordinal))
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.ProcedureWrongModule,
                "Procedure does not belong to this integration's module.");

        if (!await IsTargetExecutableAsync(procedure, cancellationToken).ConfigureAwait(false))
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.TargetNotExecutable,
                "Procedure target definition is missing, not registered, or not owned by the module.");

        try
        {
            await EnsureProcedurePermissionsAsync(procedure, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.ProcedurePermissionDenied,
                ex.Message);
        }

        IReadOnlyDictionary<string, object?>? parameters = request.Parameters;
        if (!string.IsNullOrWhiteSpace(procedure.ParameterSchema) &&
            OrchestrationParameterSchemaValidator.TryParseFields(procedure.ParameterSchema, out var fieldDescriptors))
        {
            parameters = OrchestrationParameterSchemaValidator.MergeDefaults(fieldDescriptors, request.Parameters);
        }

        try
        {
            OrchestrationParameterSchemaValidator.Validate(procedure.ParameterSchema, parameters);
        }
        catch (ArgumentException ex)
        {
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.InvalidParameters,
                ex.Message);
        }

        try
        {
            OrchestrationSafeValidator.ValidateParameters(parameters);
        }
        catch (ArgumentException ex)
        {
            return new IntegrationProcedureLaunchOutcome.Failed(
                IntegrationProcedureLaunchErrorCode.UnsafeOrchestrationParameters,
                ex.Message);
        }

        var integValue = integration.Id.Value;
        switch (procedure.LaunchKind)
        {
            case ProcedureLaunchKind.Workflow:
            {
                var wfId = new WorkflowDefinitionId(procedure.TargetDefinitionId);
                try
                {
                    await _workflowStartAuth.EnsureCanStartWorkflowAsync(wfId, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return new IntegrationProcedureLaunchOutcome.Failed(
                        IntegrationProcedureLaunchErrorCode.WorkflowStartForbidden,
                        ex.Message);
                }

                var dispatchResult = await _workflowDispatch.StartAsync(
                    new StartWorkflowRequest(
                        wfId,
                        parameters,
                        request.IdempotencyKey,
                        request.CorrelationId,
                        RetryOfRunId: null,
                        IntegrationId: integValue,
                        ProcedureId: procedure.Id,
                        ExecutionOrigin: RunExecutionOriginNames.Manual),
                    cancellationToken).ConfigureAwait(false);
                if (dispatchResult is null)
                    return new IntegrationProcedureLaunchOutcome.Failed(
                        IntegrationProcedureLaunchErrorCode.DispatchFailed,
                        "Workflow start dispatch did not complete; the definition may be unavailable.");

                return new IntegrationProcedureLaunchOutcome.Succeeded(
                    new IntegrationProcedureLaunchResponse(
                        dispatchResult.RunId.Value,
                        dispatchResult.WorkflowInstanceId.Value,
                        OperationInstanceId: null,
                        dispatchResult.WorkflowDefinitionId.Value,
                        dispatchResult.Status,
                        "workflow"));
            }
            case ProcedureLaunchKind.Operation:
            {
                var opId = new OperationId(procedure.TargetDefinitionId);
                try
                {
                    await _operationStartAuth.EnsureCanStartOperationAsync(opId, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return new IntegrationProcedureLaunchOutcome.Failed(
                        IntegrationProcedureLaunchErrorCode.OperationStartForbidden,
                        ex.Message);
                }

                var dispatchResult = await _operationDispatch.StartAsync(
                    new StartOperationRequest(
                        procedure.TargetDefinitionId,
                        parameters,
                        request.IdempotencyKey,
                        request.CorrelationId,
                        integValue,
                        procedure.Id,
                        ExecutionOrigin: RunExecutionOriginNames.Manual),
                    cancellationToken).ConfigureAwait(false);
                if (dispatchResult is null)
                    return new IntegrationProcedureLaunchOutcome.Failed(
                        IntegrationProcedureLaunchErrorCode.DispatchFailed,
                        "Operation start dispatch did not complete; the definition may be unavailable.");

                return new IntegrationProcedureLaunchOutcome.Succeeded(
                    new IntegrationProcedureLaunchResponse(
                        dispatchResult.RunId.Value,
                        WorkflowInstanceId: null,
                        dispatchResult.OperationInstanceId.Value,
                        dispatchResult.OperationDefinitionId.Value,
                        dispatchResult.Status,
                        "operation"));
            }
            default:
                return new IntegrationProcedureLaunchOutcome.Failed(
                    IntegrationProcedureLaunchErrorCode.DispatchFailed,
                    $"Unsupported launch kind '{procedure.LaunchKind}'.");
        }
    }

    private static IReadOnlyList<string> BuildBlockedCodes(
        string? integrationBlockedReason,
        bool targetOk,
        bool procedurePermissionsOk,
        bool executionStartOk,
        ProcedureLaunchKind launchKind)
    {
        var codes = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(integrationBlockedReason))
            codes.Add(integrationBlockedReason);
        if (!targetOk)
            codes.Add(ProcedureBlockedReasonCodes.TargetNotExecutable);
        if (!procedurePermissionsOk)
            codes.Add(ProcedureBlockedReasonCodes.InsufficientProcedurePermission);
        if (!executionStartOk)
        {
            codes.Add(launchKind == ProcedureLaunchKind.Workflow
                ? ProcedureBlockedReasonCodes.InsufficientWorkflowPermission
                : ProcedureBlockedReasonCodes.InsufficientOperationPermission);
        }

        return codes;
    }

    private static string? ResolveIntegrationBlockedReason(IntegrationReadinessProjection readiness)
    {
        if (readiness.IsRunnable)
            return null;

        if (readiness.Issues.Any(i =>
                string.Equals(i.Code, IntegrationReadinessIssueCodes.IntegrationNotActive, StringComparison.Ordinal) &&
                string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal)))
        {
            return ProcedureBlockedReasonCodes.IntegrationInactive;
        }

        if (readiness.Issues.Any(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal)))
            return ProcedureBlockedReasonCodes.IntegrationHardBlocked;

        return null;
    }

    /// <summary>
    /// Whether the current actor may start the procedure's target (mirrors launch-time auth; list/detail stay truthful).
    /// </summary>
    private async ValueTask<bool> HasExecutionStartPermissionAsync(ProcedureDefinition procedure, CancellationToken cancellationToken)
    {
        try
        {
            switch (procedure.LaunchKind)
            {
                case ProcedureLaunchKind.Workflow:
                    await _workflowStartAuth
                        .EnsureCanStartWorkflowAsync(new WorkflowDefinitionId(procedure.TargetDefinitionId), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                case ProcedureLaunchKind.Operation:
                    await _operationStartAuth
                        .EnsureCanStartOperationAsync(new OperationId(procedure.TargetDefinitionId), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                default:
                    return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async ValueTask EnsureProcedurePermissionsAsync(ProcedureDefinition procedure, CancellationToken cancellationToken)
    {
        if (procedure.RequiredPermissions is not { Count: > 0 } required)
            return;

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _permissionEvaluator.EvaluateAllAsync(actor, required, cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized for this procedure.");
    }

    private async ValueTask<bool> HasProcedurePermissionsAsync(ProcedureDefinition procedure, CancellationToken cancellationToken)
    {
        if (procedure.RequiredPermissions is not { Count: > 0 })
            return true;

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;

        return await _permissionEvaluator.EvaluateAllAsync(actor, procedure.RequiredPermissions, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> IsTargetExecutableAsync(ProcedureDefinition procedure, CancellationToken cancellationToken)
    {
        switch (procedure.LaunchKind)
        {
            case ProcedureLaunchKind.Workflow:
            {
                var def = await _workflowDefinitions.GetByIdAsync(new WorkflowDefinitionId(procedure.TargetDefinitionId), cancellationToken);
                return def is not null &&
                       string.Equals(def.ModuleId, procedure.ModuleId, StringComparison.Ordinal);
            }
            case ProcedureLaunchKind.Operation:
            {
                var op = await _operations.GetOperationAsync(new OperationId(procedure.TargetDefinitionId), cancellationToken);
                return op is not null &&
                       string.Equals(op.ModuleId, procedure.ModuleId, StringComparison.Ordinal);
            }
            default:
                return false;
        }
    }

    private static IntegrationProcedureSummaryDto ToSummary(ProcedureDefinition p, bool launchable, IReadOnlyList<string> blocked) =>
        new(
            p.Id,
            p.DisplayName,
            p.Description,
            p.Category,
            LaunchKindToken(p.LaunchKind),
            p.TargetDefinitionId,
            launchable,
            blocked);

    private static string LaunchKindToken(ProcedureLaunchKind kind) =>
        kind switch
        {
            ProcedureLaunchKind.Workflow => "workflow",
            ProcedureLaunchKind.Operation => "operation",
            _ => kind.ToString().ToLowerInvariant()
        };

    private static IReadOnlyList<IntegrationProcedureParameterFieldDto> ParseParameterFields(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson) ||
            !OrchestrationParameterSchemaValidator.TryParseFields(schemaJson, out var fields))
            return Array.Empty<IntegrationProcedureParameterFieldDto>();

        return fields
            .Select(f => new IntegrationProcedureParameterFieldDto(
                f.Key,
                f.Type,
                f.Required,
                f.AllowedValues,
                f.Min,
                f.Max,
                f.MaxLength,
                f.Help,
                f.Label,
                f.DefaultJson))
            .ToList();
    }
}
