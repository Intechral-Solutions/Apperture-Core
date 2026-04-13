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
    private readonly IBindingRepository _bindingRepository;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IConnectorDefinitionRegistry _connectorDefinitions;

    public ConnectionsController(
        IConnectionManagementService connectionService,
        IBindingRepository bindingRepository,
        IIntegrationRepository integrationRepository,
        IConnectorDefinitionRegistry connectorDefinitions)
    {
        _connectionService = connectionService;
        _bindingRepository = bindingRepository;
        _integrationRepository = integrationRepository;
        _connectorDefinitions = connectorDefinitions;
    }

    /// <summary>
    /// Lists connections visible to the current actor, including Used In projection.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var connections = await _connectionService.ListConnectionsAsync(cancellationToken);
            var result = new List<ConnectionDto>();
            foreach (var c in connections)
            {
                var bindings = await _bindingRepository.GetByConnectionIdAsync(c.Id, cancellationToken);
                result.Add(await ToDtoAsync(c, bindings, cancellationToken));
            }
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets a connection by id, including Used In projection.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConnectionDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _connectionService.GetConnectionAsync(new ConnectionId(id), cancellationToken);
            if (connection is null)
                return NotFound();
            var bindings = await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken);
            return Ok(await ToDtoAsync(connection, bindings, cancellationToken));
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
                body.OwnerId,
                body.SecretValues);
            var connection = await _connectionService.CreateConnectionAsync(request, cancellationToken);
            var bindings = await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = connection.Id.Value }, await ToDtoAsync(connection, bindings, cancellationToken));
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
            var request = new UpdateConnectionRequest(body.Name, body.Config, body.SecretRefs, body.SecretValues);
            var connection = await _connectionService.UpdateConnectionAsync(new ConnectionId(id), request, cancellationToken);
            var bindings = await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken);
            return Ok(await ToDtoAsync(connection, bindings, cancellationToken));
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

    /// <summary>
    /// Runs connector-specific test-connect and returns the persisted verification result.
    /// </summary>
    [HttpPost("{id}/test-connect")]
    public async Task<ActionResult<ConnectionDto>> TestConnect(string id, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _connectionService.VerifyConnectionAsync(new ConnectionId(id), cancellationToken);
            var bindings = await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken);
            return Ok(await ToDtoAsync(connection, bindings, cancellationToken));
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

    private async Task<ConnectionDto> ToDtoAsync(
        Core.Domain.Connector.Connection c,
        IReadOnlyList<Binding> bindings,
        CancellationToken cancellationToken)
    {
        var usedIn = new List<ConnectionUsedInEntry>();
        foreach (var b in bindings)
        {
            string? integrationDisplayName = null;
            if (b.IntegrationId is { } integrationId)
            {
                var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken);
                integrationDisplayName = integration?.DisplayName;
            }
            usedIn.Add(new ConnectionUsedInEntry(b.ModuleId, b.SlotName, b.ContractType, b.IntegrationId?.Value, integrationDisplayName));
        }
        return new ConnectionDto(
            c.Id.Value,
            c.ConnectorDefinitionId.Value,
            c.Name,
            c.Config,
            c.SecretRefs,
            c.ScopeKey,
            c.OwnerId,
            c.State.ToString(),
            IsVerificationSupported(c.ConnectorDefinitionId),
            c.LastVerificationStatus.ToString(),
            c.LastVerificationAtUtc,
            c.LastVerificationSummary,
            usedIn);
    }

    private bool IsVerificationSupported(ConnectorDefinitionId connectorDefinitionId)
    {
        var def = _connectorDefinitions.Get(connectorDefinitionId);
        return def?.Capabilities.HasFlag(ConnectorCapabilities.SupportsConnectionVerification) == true;
    }
}

/// <summary>
/// Represents where a connection is consumed (e.g. module slot binding). Epic 48, Epic 50.
/// </summary>
public sealed record ConnectionUsedInEntry(
    string ModuleId,
    string SlotName,
    string ContractType,
    string? IntegrationId = null,
    string? IntegrationDisplayName = null);

public sealed record ConnectionDto(
    string Id,
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    string State,
    bool SupportsVerification,
    string LastVerificationStatus,
    DateTimeOffset? LastVerificationAtUtc,
    string? LastVerificationSummary,
    IReadOnlyList<ConnectionUsedInEntry> UsedIn);

public sealed record CreateConnectionRequestBody(
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    IReadOnlyDictionary<string, string>? SecretValues = null);

public sealed record UpdateConnectionRequestBody(
    string? Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs,
    IReadOnlyDictionary<string, string>? SecretValues = null);
