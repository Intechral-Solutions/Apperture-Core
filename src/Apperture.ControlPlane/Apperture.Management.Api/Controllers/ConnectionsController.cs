using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
using Client = Apperture.Management.Client;
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
    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceOwnershipMoveService _moveService;

    public ConnectionsController(
        IConnectionManagementService connectionService,
        IBindingRepository bindingRepository,
        IIntegrationRepository integrationRepository,
        IConnectorDefinitionRegistry connectorDefinitions,
        IResourceContextService resourceContexts,
        IResourceOwnershipMoveService moveService)
    {
        _connectionService = connectionService;
        _bindingRepository = bindingRepository;
        _integrationRepository = integrationRepository;
        _connectorDefinitions = connectorDefinitions;
        _resourceContexts = resourceContexts;
        _moveService = moveService;
    }

    /// <summary>
    /// Lists connections visible to the current actor, including Used In projection.
    /// </summary>
    /// <param name="contextId">
    /// Optional owning ResourceContext filter. Omitted (the default) returns the same global set this
    /// endpoint has always returned. An unknown ResourceContext is rejected with 400.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionDto>>> List(
        [FromQuery] string? contextId,
        CancellationToken cancellationToken)
    {
        try
        {
            ResourceContextId? contextFilter = null;
            if (!string.IsNullOrWhiteSpace(contextId))
            {
                contextFilter = new ResourceContextId(contextId);
                if (await _resourceContexts.GetAsync(contextFilter, cancellationToken) is null)
                    return BadRequest(new { error = $"ResourceContext '{contextId}' not found." });
            }

            var connections = await _connectionService.ListConnectionsAsync(contextFilter, cancellationToken);
            // One query for the small ResourceContext dimension, reused for every row (no N+1).
            var contexts = await _resourceContexts.ListAsync(includeArchived: true, cancellationToken);
            var result = new List<ConnectionDto>();
            foreach (var c in connections)
            {
                var bindings = await _bindingRepository.GetByConnectionIdAsync(c.Id, cancellationToken);
                result.Add(await ToDtoAsync(c, bindings, contexts, cancellationToken));
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
                body.SecretValues,
                string.IsNullOrWhiteSpace(body.ResourceContextId) ? null : new ResourceContextId(body.ResourceContextId));
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

    /// <summary>Advisory preview of moving this Connection to another ResourceContext (design gate §8.2).</summary>
    [HttpGet("{id}/ownership/move-preview")]
    public async Task<IActionResult> GetOwnershipMovePreview(
        string id, [FromQuery] string targetResourceContextId, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.PreviewConnectionMoveAsync(
                new ConnectionId(id), new ResourceContextId(targetResourceContextId), cancellationToken);
            return Ok(ResourceMoveHttpMapper.ToDto(preview));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Moves this Connection to another ResourceContext. Re-validates identically inside the gated transaction (§8.2).</summary>
    [HttpPost("{id}/ownership/move")]
    public async Task<IActionResult> PostOwnershipMove(
        string id, [FromBody] Client.ResourceOwnershipMoveRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.MoveConnectionAsync(
                new ConnectionId(id), new ResourceContextId(body.TargetResourceContextId), cancellationToken);
            return ResourceMoveHttpMapper.MapMoveResult(preview);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ResourceOwnershipBusyException ex) { return ResourceMoveHttpMapper.MapBusy(ex); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    private async Task<ConnectionDto> ToDtoAsync(
        Core.Domain.Connector.Connection c,
        IReadOnlyList<Binding> bindings,
        CancellationToken cancellationToken)
    {
        var contexts = await _resourceContexts.ListAsync(includeArchived: true, cancellationToken);
        return await ToDtoAsync(c, bindings, contexts, cancellationToken);
    }

    private async Task<ConnectionDto> ToDtoAsync(
        Core.Domain.Connector.Connection c,
        IReadOnlyList<Binding> bindings,
        IReadOnlyList<ResourceContext> resourceContexts,
        CancellationToken cancellationToken)
    {
        var usedIn = new List<ConnectionUsedInEntry>();
        foreach (var b in bindings)
        {
            string? integrationDisplayName = null;
            string? integrationResourceContextId = null;
            if (b.IntegrationId is { } integrationId)
            {
                var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken);
                integrationDisplayName = integration?.DisplayName;
                integrationResourceContextId = integration?.ResourceContextId.Value;
            }
            usedIn.Add(new ConnectionUsedInEntry(b.ModuleId, b.SlotName, b.ContractType, b.IntegrationId?.Value, integrationDisplayName, integrationResourceContextId));
        }
        var owning = resourceContexts.FirstOrDefault(rc => string.Equals(rc.Id.Value, c.ResourceContextId.Value, StringComparison.Ordinal));
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
            usedIn,
            c.ResourceContextId.Value,
            owning?.ToRef(),
            ConnectorDisplayName(c.ConnectorDefinitionId));
    }

    /// <summary>
    /// The connector's human-readable name from the in-memory registry (no I/O). Null when this host does not
    /// register the connector: the workspace then falls back to the raw id rather than inventing a name.
    /// </summary>
    private string? ConnectorDisplayName(ConnectorDefinitionId connectorDefinitionId) =>
        _connectorDefinitions.Get(connectorDefinitionId)?.Name;

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
    string? IntegrationDisplayName = null,
    string? IntegrationResourceContextId = null);

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
    IReadOnlyList<ConnectionUsedInEntry> UsedIn,
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null,
    string? ConnectorDisplayName = null);

/// <param name="ScopeKey">Authorization scope. Unrelated to <paramref name="ResourceContextId"/>.</param>
/// <param name="ResourceContextId">
/// Optional owning ResourceContext. Omitted means the reserved Shared context, so existing clients
/// remain compatible. An unknown or archived context is rejected.
/// </param>
public sealed record CreateConnectionRequestBody(
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    IReadOnlyDictionary<string, string>? SecretValues = null,
    string? ResourceContextId = null);

public sealed record UpdateConnectionRequestBody(
    string? Name,
    IReadOnlyDictionary<string, string>? Config,
    IReadOnlyDictionary<string, string>? SecretRefs,
    IReadOnlyDictionary<string, string>? SecretValues = null);
