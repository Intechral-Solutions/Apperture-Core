using Apperture.ControlPlane.Security;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("modules")]
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
            var manifests = await _listingService.GetModulesAsync(cancellationToken);
            return Ok(manifests.Select(ToModuleSummary).ToList());
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

    private static ModuleSummaryDto ToModuleSummary(ModuleManifest m) => new(m.Id, m.Name, m.Version, m.Description);
    private static WorkflowSummaryDto ToWorkflowSummary(WorkflowDefinition w) => new(w.Id.Value, w.Name, w.ModuleId, w.StepNames.ToList(), w.Category.ToString());
}

public sealed record ModuleSummaryDto(string Id, string Name, string Version, string? Description);
public sealed record WorkflowSummaryDto(string Id, string Name, string ModuleId, IReadOnlyList<string> StepNames, string Category);
