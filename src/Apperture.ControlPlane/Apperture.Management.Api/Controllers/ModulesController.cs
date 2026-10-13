using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/modules")]
[Authorize]
public sealed class ModulesController : ControllerBase
{
    private readonly IModuleWorkflowListingService _listingService;

    public ModulesController(IModuleWorkflowListingService listingService)
    {
        _listingService = listingService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModuleSummaryDto>>> GetModules(CancellationToken cancellationToken)
    {
        try
        {
            var modules = await _listingService.GetModulesAsync(cancellationToken);
            return Ok(modules.Select(ToModuleSummary).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ModuleSummaryDto>> GetModule(string id, CancellationToken cancellationToken)
    {
        try
        {
            var module = await _listingService.GetModuleAsync(id, cancellationToken);
            if (module is null)
                return NotFound();
            return Ok(ToModuleSummary(module));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}/workflows")]
    public async Task<ActionResult<IEnumerable<WorkflowSummaryDto>>> GetWorkflows(string id, CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await _listingService.GetWorkflowsForModuleAsync(id, cancellationToken);
            return Ok(definitions.Select(ToWorkflowSummary).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{id}/install")]
    public Task<ActionResult<ModuleLifecycleActionResultDto>> InstallModule(string id, CancellationToken cancellationToken)
        => ExecuteLifecycleAction(() => _listingService.InstallModuleAsync(id, cancellationToken));

    [HttpPost("{id}/enable")]
    public Task<ActionResult<ModuleLifecycleActionResultDto>> EnableModule(string id, CancellationToken cancellationToken)
        => ExecuteLifecycleAction(() => _listingService.EnableModuleAsync(id, cancellationToken));

    [HttpPost("{id}/disable")]
    public Task<ActionResult<ModuleLifecycleActionResultDto>> DisableModule(string id, CancellationToken cancellationToken)
        => ExecuteLifecycleAction(() => _listingService.DisableModuleAsync(id, cancellationToken));

    [HttpPost("{id}/uninstall")]
    public Task<ActionResult<ModuleLifecycleActionResultDto>> UninstallModule(string id, CancellationToken cancellationToken)
        => ExecuteLifecycleAction(() => _listingService.UninstallModuleAsync(id, cancellationToken));

    private async Task<ActionResult<ModuleLifecycleActionResultDto>> ExecuteLifecycleAction(
        Func<ValueTask<ModuleLifecycleTransitionResult>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            return Ok(new ModuleLifecycleActionResultDto(
                result.Succeeded,
                result.ModuleId,
                result.Action,
                result.State.ToString(),
                result.Effect.ToString(),
                result.Blockers.Select(x => new ModuleLifecycleBlockerDto(x.Code, x.Message)).ToList()));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            return BadRequest(new ModuleLifecycleActionResultDto(false, "", "unknown", "Discovered", ModuleLifecycleEffect.Immediate.ToString(), [new("invalid_operation", ex.Message)]));
        }
    }

    private static ModuleSummaryDto ToModuleSummary(ModuleCatalogEntry module) => new(
        module.Descriptor.ModuleId,
        module.Descriptor.DisplayName,
        module.Descriptor.Version,
        module.Descriptor.Description,
        module.State.ToString(),
        module.Enabled,
        module.Installed,
        module.EffectivelyActive,
        module.ValidationIssues.Select(x => new ModuleLifecycleBlockerDto(x.Code, x.Message)).ToList(),
        module.Descriptor.Contributions.GroupBy(x => x.Kind, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal));

    private static WorkflowSummaryDto ToWorkflowSummary(WorkflowDefinition w) => new(w.Id.Value, w.Name, w.ModuleId, w.StepNames.ToList(), w.Category.ToString());
}

public sealed record ModuleSummaryDto(
    string Id,
    string Name,
    string Version,
    string? Description,
    string State,
    bool Enabled,
    bool Installed,
    bool EffectivelyActive,
    IReadOnlyList<ModuleLifecycleBlockerDto> ValidationIssues,
    IReadOnlyDictionary<string, int> ContributionCounts);
public sealed record WorkflowSummaryDto(string Id, string Name, string ModuleId, IReadOnlyList<string> StepNames, string Category);
public sealed record ModuleLifecycleActionResultDto(
    bool Succeeded,
    string ModuleId,
    string Action,
    string State,
    string Effect,
    IReadOnlyList<ModuleLifecycleBlockerDto> Blockers);
public sealed record ModuleLifecycleBlockerDto(string Code, string Message);
