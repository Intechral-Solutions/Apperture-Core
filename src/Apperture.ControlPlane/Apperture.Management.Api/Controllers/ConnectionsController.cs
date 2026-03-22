using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/connections")]
[Authorize]
public sealed class ConnectionsController : ControllerBase
{
    private readonly IConnectionManagementService _connectionService;

    public ConnectionsController(IConnectionManagementService connectionService)
    {
        _connectionService = connectionService;
    }

    /// <summary>
    /// Lists connections visible to the current actor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var connections = await _connectionService.ListConnectionsAsync(cancellationToken);
            return Ok(connections.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets a connection by id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConnectionDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _connectionService.GetConnectionAsync(new ConnectionId(id), cancellationToken);
            if (connection is null)
                return NotFound();
            return Ok(ToDto(connection));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a new connection.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ConnectionDto>> Create([FromBody] CreateConnectionRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateConnectionRequest(
                new ConnectorDefinitionId(body.ConnectorDefinitionId),
                body.Name,
                body.Config ?? new Dictionary<string, string>(),
                body.SecretRefs ?? new Dictionary<string, string>(),
                body.ScopeKey,
                body.OwnerId);
            var connection = await _connectionService.CreateConnectionAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = connection.Id.Value }, ToDto(connection));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Updates connection metadata/config.
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<ConnectionDto>> Update(string id, [FromBody] UpdateConnectionRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var request = new UpdateConnectionRequest(body.Name, body.Config, body.SecretRefs);
            var connection = await _connectionService.UpdateConnectionAsync(new ConnectionId(id), request, cancellationToken);
            return Ok(ToDto(connection));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retires a connection.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Retire(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _connectionService.RetireConnectionAsync(new ConnectionId(id), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private static ConnectionDto ToDto(Core.Domain.Connector.Connection c) => new(
        c.Id.Value,
        c.ConnectorDefinitionId.Value,
        c.Name,
        c.Config,
        c.SecretRefs,
        c.ScopeKey,
        c.OwnerId,
        c.State.ToString());
}

public sealed record ConnectionDto(
    string Id,
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    string State);

public sealed record CreateConnectionRequestBody(
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs,
    string? ScopeKey,
    string? OwnerId);

public sealed record UpdateConnectionRequestBody(
    string? Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs);
