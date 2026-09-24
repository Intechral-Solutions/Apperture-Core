using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Which typed tools a Connection offers to the caller. Requires <c>connection.read</c> AND <c>connection.browse</c> on the
/// Connection, evaluated here on the server for every request, and answers identically to a caller lacking either whether or
/// not the connector has a tool, so tool availability is never disclosed to a caller without <c>connection.browse</c>.
/// </summary>
[ApiController]
[Route("api/connections/{id}/tools")]
[Authorize]
public sealed class ConnectionToolsController : ControllerBase
{
    private readonly IConnectionToolAccess _tools;

    public ConnectionToolsController(IConnectionToolAccess tools) => _tools = tools;

    [HttpGet]
    public async Task<ActionResult<ConnectionToolsDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var tools = await _tools.GetToolsAsync(new ConnectionId(id), cancellationToken).ConfigureAwait(false);
            return tools is null ? NotFound() : Ok(new ConnectionToolsDto(tools.SqlStructureBrowser));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}

/// <summary>Typed tool flags for one Connection. One member per real tool; deliberately not a generic capability list.</summary>
public sealed record ConnectionToolsDto(bool SqlStructureBrowser);
