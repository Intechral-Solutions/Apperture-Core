using Apperture.ControlPlane.Security.DemoBusinessRecords;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Demo/sample business record projection API (BDS-3).
/// Not the final platform business-entity surface.
/// </summary>
[ApiController]
[Route("api/demo/business-records")]
[Authorize]
public sealed class DemoBusinessRecordsController : ControllerBase
{
    private readonly IDemoBusinessRecordProjectionService _projection;

    public DemoBusinessRecordsController(IDemoBusinessRecordProjectionService projection) =>
        _projection = projection;

    [HttpGet("customers")]
    [ProducesResponseType(typeof(DemoBusinessRecordListResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DemoBusinessRecordListResult>> ListCustomers(
        [FromQuery] string? q,
        [FromQuery] string? sourceKind,
        [FromQuery] bool? hasConflict,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _projection.ListCustomersAsync(
                new DemoBusinessRecordListQuery(q, sourceKind, hasConflict, skip, take),
                cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("customers/{businessKey}")]
    [ProducesResponseType(typeof(DemoBusinessRecordProjection), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DemoBusinessRecordProjection>> GetCustomer(
        string businessKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _projection.GetCustomerAsync(businessKey, cancellationToken).ConfigureAwait(false);
            if (item is null)
                return NotFound();
            return Ok(item);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
