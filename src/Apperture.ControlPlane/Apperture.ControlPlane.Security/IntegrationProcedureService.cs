using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Application.Validation;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Procedures;
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

        if (!readiness.IsRunnable)
            return new IntegrationProcedureListResponse(false, Array.Empty<IntegrationProcedureSummaryDto>());

        var moduleProcedures = await _procedures.GetByModuleAsync(integration.ModuleId, cancellationToken);
        var list = new List<IntegrationProcedureSummaryDto>();
        foreach (var p in moduleProcedures.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (!await IsTargetExecutableAsync(p, cancellationToken).ConfigureAwait(false))
                continue;
            list.Add(ToSummary(p));
        }

        return new IntegrationProcedureListResponse(true, list);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationProcedureDetailDto?> GetProcedureAsync(string integrationId, string procedureId, CancellationToken cancellationToken = default)
    {
        var id = new IntegrationId(integrationId);
        var integration = await _integrations.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var readiness = await _readiness.GetReadinessAsync(id, cancellationToken);
        if (readiness is null)
            return null;
        if (!readiness.IsRunnable)
            throw new InvalidOperationException("Integration is not ready to run procedures.");

        var procedure = await _procedures.GetProcedureAsync(procedureId, cancellationToken);
        if (procedure is null || !string.Equals(procedure.ModuleId, integration.ModuleId, StringComparison.Ordinal))
            return null;

        if (!await IsTargetExecutableAsync(procedure, cancellationToken).ConfigureAwait(false))
            return null;

        var fields = ParseParameterFields(procedure.ParameterSchema);
        return new IntegrationProcedureDetailDto(
            procedure.Id,
            procedure.DisplayName,
            procedure.Description,
            procedure.Category,
            LaunchKindToken(procedure.LaunchKind),
            procedure.TargetDefinitionId,
            fields);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationProcedureLaunchResponse?> LaunchAsync(
        string integrationId,
        string procedureId,
        IntegrationProcedureLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = new IntegrationId(integrationId);
        var integration = await _integrations.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var readiness = await _readiness.GetReadinessAsync(id, cancellationToken);
        if (readiness is null)
            return null;
        if (!readiness.IsRunnable)
            throw new InvalidOperationException("Integration is not ready to run procedures.");

        var procedure = await _procedures.GetProcedureAsync(procedureId, cancellationToken);
        if (procedure is null || !string.Equals(procedure.ModuleId, integration.ModuleId, StringComparison.Ordinal))
            return null;

        if (!await IsTargetExecutableAsync(procedure, cancellationToken).ConfigureAwait(false))
            return null;

        await EnsureProcedurePermissionsAsync(procedure, cancellationToken).ConfigureAwait(false);

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
            throw new InvalidOperationException(ex.Message, ex);
        }

        OrchestrationSafeValidator.ValidateParameters(parameters);

        var integValue = integration.Id.Value;
        switch (procedure.LaunchKind)
        {
            case ProcedureLaunchKind.Workflow:
            {
                var wfId = new WorkflowDefinitionId(procedure.TargetDefinitionId);
                await _workflowStartAuth.EnsureCanStartWorkflowAsync(wfId, cancellationToken).ConfigureAwait(false);
                var dispatchResult = await _workflowDispatch.StartAsync(
                    new StartWorkflowRequest(
                        wfId,
                        parameters,
                        request.IdempotencyKey,
                        request.CorrelationId,
                        RetryOfRunId: null,
                        IntegrationId: integValue,
                        ProcedureId: procedure.Id),
                    cancellationToken).ConfigureAwait(false);
                if (dispatchResult is null)
                    return null;

                return new IntegrationProcedureLaunchResponse(
                    dispatchResult.RunId.Value,
                    dispatchResult.WorkflowInstanceId.Value,
                    OperationInstanceId: null,
                    dispatchResult.WorkflowDefinitionId.Value,
                    dispatchResult.Status,
                    "workflow");
            }
            case ProcedureLaunchKind.Operation:
            {
                var opId = new OperationId(procedure.TargetDefinitionId);
                await _operationStartAuth.EnsureCanStartOperationAsync(opId, cancellationToken).ConfigureAwait(false);
                var dispatchResult = await _operationDispatch.StartAsync(
                    new StartOperationRequest(
                        procedure.TargetDefinitionId,
                        parameters,
                        request.IdempotencyKey,
                        request.CorrelationId,
                        integValue,
                        procedure.Id),
                    cancellationToken).ConfigureAwait(false);
                if (dispatchResult is null)
                    return null;

                return new IntegrationProcedureLaunchResponse(
                    dispatchResult.RunId.Value,
                    WorkflowInstanceId: null,
                    dispatchResult.OperationInstanceId.Value,
                    dispatchResult.OperationDefinitionId.Value,
                    dispatchResult.Status,
                    "operation");
            }
            default:
                throw new InvalidOperationException($"Unsupported launch kind '{procedure.LaunchKind}'.");
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

    private static IntegrationProcedureSummaryDto ToSummary(ProcedureDefinition p) =>
        new(
            p.Id,
            p.DisplayName,
            p.Description,
            p.Category,
            LaunchKindToken(p.LaunchKind),
            p.TargetDefinitionId);

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
