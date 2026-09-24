using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Which typed tools a Connection offers to the caller, and the SQL structure browser's read-only requests. Every endpoint
/// requires <c>connection.read</c> AND <c>connection.browse</c> on the Connection, evaluated on the server for every request.
/// Refusals are identical whether or not the connector has a tool, so tool availability is never disclosed to a caller without
/// <c>connection.browse</c>. Structure requests return metadata only: there is no endpoint that reads rows, takes SQL, or changes
/// anything, and each request writes one audit record.
/// <para>
/// Each structure request logs in to an external system, consumes browse capacity and writes an audit record, so it is a POST, never
/// a GET that a cross-site navigation could trigger. The platform has no antiforgery-token middleware; its request-forgery posture is
/// the SameSite=Lax session cookie (not sent on cross-site POSTs) plus a required typed JSON body: a non-JSON or missing content type is
/// a 415 and an empty JSON body is a 400 (<c>[Consumes]</c>, the body binder and <c>[ApiController]</c>), all before the action runs,
/// so a plain cross-site HTML form cannot reach the provider or the audit. Only the local availability lookup remains a GET.
/// </para>
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

    /// <summary>Schemas in the database the Connection points at.</summary>
    [HttpPost("sql-structure/schemas")]
    [Consumes("application/json")]
    public Task<ActionResult<SqlStructureResponseDto<SqlSchemaDto>>> SqlSchemas(
        string id, [FromBody] SqlSchemasRequestDto request, CancellationToken cancellationToken) =>
        RunAsync(
            () => _tools.ListSqlSchemasAsync(new ConnectionId(id), cancellationToken),
            s => new SqlSchemaDto(s.Name));

    /// <summary>Tables and views in one schema. The schema is a VALUE, bound as a parameter; it is never part of any SQL text.</summary>
    [HttpPost("sql-structure/tables")]
    [Consumes("application/json")]
    public Task<ActionResult<SqlStructureResponseDto<SqlTableDto>>> SqlTables(
        string id, [FromBody] SqlTablesRequestDto request, CancellationToken cancellationToken) =>
        RunAsync(
            () => _tools.ListSqlTablesAsync(new ConnectionId(id), request.Schema ?? "", cancellationToken),
            t => new SqlTableDto(t.Schema, t.Name, t.IsView));

    /// <summary>Columns of one table or view. Schema and table are VALUES, bound as parameters.</summary>
    [HttpPost("sql-structure/columns")]
    [Consumes("application/json")]
    public Task<ActionResult<SqlStructureResponseDto<SqlColumnDto>>> SqlColumns(
        string id, [FromBody] SqlColumnsRequestDto request, CancellationToken cancellationToken) =>
        RunAsync(
            () => _tools.ListSqlColumnsAsync(new ConnectionId(id), request.Schema ?? "", request.Table ?? "", cancellationToken),
            c => new SqlColumnDto(c.Name, c.DataType, c.IsNullable, c.Ordinal, c.MaxLength));

    private async Task<ActionResult<SqlStructureResponseDto<TDto>>> RunAsync<TInfo, TDto>(
        Func<ValueTask<SqlStructureResult<TInfo>?>> call,
        Func<TInfo, TDto> map)
    {
        try
        {
            var result = await call().ConfigureAwait(false);
            if (result is null)
                return NotFound();

            // A remote refusal or outage is the remote system's answer: HTTP 200 with an outcome and a fixed message, never a 500.
            return Ok(new SqlStructureResponseDto<TDto>(
                result.Outcome.ToString(), result.Items.Select(map).ToList(), result.Truncated, result.Limit, result.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ConnectionToolNotAvailableException)
        {
            return NotFound();
        }
        catch (ConnectionToolRequestException ex)
        {
            // Only the entry point's own validation failures are echoed: their messages are fixed, and a provider's exception
            // text (which could carry remote text) is never returned.
            return BadRequest(new { error = ex.Message });
        }
        catch (ConnectionToolBusyException)
        {
            Response.Headers.RetryAfter = "2";
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many structure requests are running for this connection. Try again shortly." });
        }
    }
}

/// <summary>Typed tool flags for one Connection. One member per real tool; deliberately not a generic capability list.</summary>
public sealed record ConnectionToolsDto(bool SqlStructureBrowser);

/// <summary>One structure request's result. <c>Outcome</c> is a <see cref="ConnectionToolOutcome"/> name; <c>Message</c> is a fixed safe sentence.</summary>
public sealed record SqlStructureResponseDto<T>(string Outcome, IReadOnlyList<T> Items, bool Truncated, int Limit, string? Message);

/// <summary>Body of a schema-listing request. Deliberately empty: a JSON body is still required (see the controller remarks).</summary>
public sealed record SqlSchemasRequestDto();

/// <summary>Body of a table-listing request. The schema is a VALUE, bound as a parameter; it is never part of any SQL text.</summary>
public sealed record SqlTablesRequestDto(string? Schema);

/// <summary>Body of a column-listing request. Schema and table are VALUES, bound as parameters.</summary>
public sealed record SqlColumnsRequestDto(string? Schema, string? Table);

public sealed record SqlSchemaDto(string Name);

public sealed record SqlTableDto(string Schema, string Name, bool IsView);

public sealed record SqlColumnDto(string Name, string DataType, bool IsNullable, int Ordinal, int? MaxLength);
