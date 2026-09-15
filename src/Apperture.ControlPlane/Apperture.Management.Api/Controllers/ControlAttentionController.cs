using Apperture.Core.Application.ControlFacade;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class ControlAttentionController : ControllerBase
{
    private readonly IControlAttentionQueryService _attention;

    public ControlAttentionController(IControlAttentionQueryService attention)
    {
        _attention = attention;
    }

    /// <summary>
    /// Cross-domain attention items for operator triage (permission-safe).
    /// </summary>
    [HttpGet("attention")]
    [ProducesResponseType(typeof(IReadOnlyList<ControlAttentionItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ControlAttentionItemDto>>> GetAttention(
        [FromQuery] DateTimeOffset? createdFromUtc,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        var from = createdFromUtc ?? DateTimeOffset.UtcNow.AddHours(-24);
        var items = await _attention.ListAttentionAsync(from, Math.Max(1, Math.Min(take, 100)), cancellationToken: cancellationToken).ConfigureAwait(false);
        return Ok(items);
    }
}

