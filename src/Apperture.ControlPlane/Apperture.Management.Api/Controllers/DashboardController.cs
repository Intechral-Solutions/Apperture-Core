using Apperture.ControlPlane.Dashboard;
using Apperture.Management.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardDefaultViewService _dashboard;

    public DashboardController(IDashboardDefaultViewService dashboard) => _dashboard = dashboard;

    [HttpGet("default")]
    [ProducesResponseType(typeof(DashboardDefaultResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDefaultResponse>> GetDefault(CancellationToken cancellationToken = default)
    {
        var view = await _dashboard.GetDefaultAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new DashboardDefaultResponse
        {
            Scope = view.Scope,
            Title = view.Title,
            ProviderFailureCount = view.ProviderFailureCount,
            Cards = view.Cards
                .OrderBy(x => x.Order)
                .Select(c => new DashboardCardResponse
                {
                    Id = c.Id,
                    Type = c.Type,
                    Title = c.Title,
                    Order = c.Order,
                    NavigationHref = c.NavigationHref,
                    NavigationLabel = c.NavigationLabel,
                    Attention = c.Attention.ToString(),
                    Summary = c.Summary,
                    IsPlaceholder = c.IsPlaceholder,
                    PlaceholderReason = c.PlaceholderReason,
                    Facts = c.Facts.Select(f => new DashboardCardFactResponse
                    {
                        Label = f.Label,
                        Value = f.Value
                    }).ToList()
                })
                .ToList()
        });
    }
}
