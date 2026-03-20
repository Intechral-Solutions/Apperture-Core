using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Trust;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class ControlCenterQueryService : IControlCenterQueryService
{
    private readonly IOperationDefinitionQueryService _operations;
    private readonly IModuleWorkflowListingService _workflowListing;
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IPrincipalContext _principal;
    private readonly IEnvironmentContext _environment;
    private readonly IControlActivityQueryService _activity;
    private readonly IControlAttentionQueryService _attention;
    private readonly IControlActionAvailabilityProjection _actions;

    public ControlCenterQueryService(
        IOperationDefinitionQueryService operations,
        IModuleWorkflowListingService workflowListing,
        IRuntimeIdentityService runtimeIdentity,
        IAuthorizationEvaluationService authorization,
        IPrincipalContext principal,
        IEnvironmentContext environment,
        IControlActivityQueryService activity,
        IControlAttentionQueryService attention,
        IControlActionAvailabilityProjection actions)
    {
        _operations = operations;
        _workflowListing = workflowListing;
        _runtimeIdentity = runtimeIdentity;
        _authorization = authorization;
        _principal = principal;
        _environment = environment;
        _activity = activity;
        _attention = attention;
        _actions = actions;
    }

    public async ValueTask<ControlCenterDto> GetControlCenterAsync(
        DateTimeOffset createdFromUtc,
        int takeRecentActivity,
        int takeAttention,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return new ControlCenterDto([], [], []);

        var targets = new List<ControlTargetSummaryDto>(32);

        // Operations: already permission-filtered by OperationDefinitionQueryService.
        var operationDefs = await _operations.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var op in operationDefs)
        {
            var actions = await _actions.ProjectOperationDefinitionActionsAsync(op, cancellationToken).ConfigureAwait(false);
            targets.Add(new ControlTargetSummaryDto(
                TargetKind: ControlFacade.TargetKindOperation,
                TargetId: op.OperationDefinitionId,
                DisplayName: op.DisplayName,
                GroupKey: op.ModuleId,
                DetailLink: $"/operations/start?operationDefinitionId={System.Uri.EscapeDataString(op.OperationDefinitionId)}",
                AvailableActions: actions));
        }

        // Workflows: list visible workflows, then compute start action availability per workflow definition.
        var modules = await _workflowListing.GetModulesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var module in modules)
        {
            var workflows = await _workflowListing.GetWorkflowsForModuleAsync(module.Id, cancellationToken).ConfigureAwait(false);
            foreach (var wf in workflows)
            {
                var startActions = await _actions.ProjectWorkflowDefinitionActionsAsync(wf, cancellationToken).ConfigureAwait(false);
                targets.Add(new ControlTargetSummaryDto(
                    TargetKind: ControlFacade.TargetKindWorkflow,
                    TargetId: wf.Id.Value,
                    DisplayName: wf.Name,
                    GroupKey: wf.ModuleId,
                        DetailLink: $"/workflows/start?moduleId={System.Uri.EscapeDataString(wf.ModuleId)}&workflowId={System.Uri.EscapeDataString(wf.Id.Value)}",
                    AvailableActions: startActions));
            }
        }

        // Runtime instances: permission-gated by runtime.read, then shutdown request action availability depends on capability + runtime.control.shutdown.request.
        var envId = _environment.EnvironmentId;
        var runtimes = await _runtimeIdentity.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var runtime in runtimes)
        {
            var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(envId, runtime.Id);
            var permission = new PermissionDescriptor(
                ActionConstants.RuntimeRead,
                new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

            var allowed = await _authorization.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, permission, permission.Resource),
                cancellationToken).ConfigureAwait(false);

            if (!allowed)
                continue;

            var rtActions = await _actions.ProjectRuntimeInstanceActionsAsync(runtime, cancellationToken).ConfigureAwait(false);
            targets.Add(new ControlTargetSummaryDto(
                TargetKind: ControlFacade.TargetKindRuntimeInstance,
                TargetId: runtime.Id,
                DisplayName: runtime.DisplayName,
                GroupKey: null,
                DetailLink: $"/runtimes/{System.Uri.EscapeDataString(runtime.Id)}/controls/shutdown/requests",
                AvailableActions: rtActions));
        }

        // Recent activity and attention items.
        var activity = await _activity.ListRecentActivityAsync(createdFromUtc, takeRecentActivity, cancellationToken).ConfigureAwait(false);
        var attention = await _attention.ListAttentionAsync(createdFromUtc, takeAttention, cancellationToken).ConfigureAwait(false);

        return new ControlCenterDto(
            Targets: targets,
            RecentActivity: activity,
            AttentionItems: attention);
    }
}

