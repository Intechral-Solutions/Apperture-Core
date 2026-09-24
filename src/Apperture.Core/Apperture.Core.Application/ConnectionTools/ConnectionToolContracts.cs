using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.ConnectionTools;

/// <summary>
/// Whether a tool provider can serve requests in the host it is registered in. This is a local, static fact about host
/// composition. It is never the result of probing the remote system.
/// </summary>
public enum ConnectionToolAvailability
{
    Available = 0,
    Unavailable = 1
}

/// <summary>
/// A connector-supplied, typed, optional tool surface for Connections of one connector. It is discovered by
/// <see cref="ConnectorDefinitionId"/> from the providers the connector package itself registers.
/// <para>
/// This is deliberately not a framework: no registry, no contributor interface, no generic command or capability list, and
/// nothing executable on the connector definition. A provider is a marker plus a typed capability interface (for example
/// <see cref="ISqlStructureBrowser"/>) implemented only by connectors that genuinely have that tool. Most connectors
/// have none, which is the normal case.
/// </para>
/// </summary>
public interface IConnectionToolProvider
{
    /// <summary>The connector whose Connections this provider serves.</summary>
    ConnectorDefinitionId ConnectorDefinitionId { get; }

    /// <summary>Local and static. Reading it never touches the network, a secret or a remote client.</summary>
    ConnectionToolAvailability Availability { get; }
}

/// <summary>How a remote tool operation ended. The remote system's own refusal is an answer, not a platform failure.</summary>
public enum ConnectionToolOutcome
{
    Succeeded = 0,

    /// <summary>The remote system refused the operation for the Connection's login (for example missing metadata permission).</summary>
    RemoteDenied = 1,

    /// <summary>The remote system could not be reached, or did not answer in time.</summary>
    RemoteUnavailable = 2,

    /// <summary>The Connection's stored secrets could not be resolved, so no remote client was created.</summary>
    SecretsUnavailable = 3,

    /// <summary>Any other failure. The reason is deliberately not carried.</summary>
    Failed = 4
}

/// <summary>One bounded page of structure metadata. <paramref name="Truncated"/> is set when more items exist than the cap.</summary>
/// <param name="Outcome">How the operation ended.</param>
/// <param name="Items">Metadata rows only. Never row or content data from the remote system.</param>
/// <param name="Truncated">True when the remote system has more than <paramref name="Limit"/> items; the list is the first <paramref name="Limit"/>.</param>
/// <param name="Limit">The fixed cap that applied to this request.</param>
/// <param name="Message">A fixed, safe sentence for a failure outcome. Never remote error text and never a secret.</param>
public sealed record SqlStructureResult<T>(
    ConnectionToolOutcome Outcome,
    IReadOnlyList<T> Items,
    bool Truncated,
    int Limit,
    string? Message = null);

public sealed record SqlSchemaInfo(string Name);

/// <summary>A table or a view in a schema.</summary>
public sealed record SqlTableInfo(string Schema, string Name, bool IsView);

/// <summary>A column of a table or view. Structure only: name, declared type and nullability.</summary>
public sealed record SqlColumnInfo(string Name, string DataType, bool IsNullable, int Ordinal, int? MaxLength);

/// <summary>
/// The one typed tool in Services v1: browse the STRUCTURE (schemas, then tables and views, then columns) of the database a
/// SQL Server Connection points at. It is read-only by construction: the implementation issues only a closed set of
/// fixed, platform-authored, parameterized metadata queries. There is no operator-supplied SQL, no row or content read, and
/// no mutation, and nothing here can be extended into a query console.
/// </summary>
public interface ISqlStructureBrowser : IConnectionToolProvider
{
    ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken);

    ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken);

    ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(Connection connection, string schema, string table, CancellationToken cancellationToken);
}

/// <summary>Which tools the caller may use on one Connection right now. A tool absent here is simply not offered.</summary>
public sealed record ConnectionToolSet(bool SqlStructureBrowser)
{
    public static ConnectionToolSet None { get; } = new(false);

    public bool Any => SqlStructureBrowser;
}

/// <summary>
/// Authorized access to a Connection's tools. Every operation requires BOTH <c>connection.read</c> and
/// <c>connection.browse</c> on the Connection; neither alone is enough. Tool availability is a local, static lookup and
/// is only ever reported to a caller who holds both.
/// </summary>
public interface IConnectionToolAccess
{
    /// <summary>
    /// The tools offered for this Connection, or null when the Connection does not exist. Throws
    /// <see cref="UnauthorizedAccessException"/> unless the caller holds <c>connection.read</c> and <c>connection.browse</c>,
    /// and does so identically whether or not a provider exists, so availability is never disclosed to anyone lacking
    /// <c>connection.browse</c>. Performs no I/O beyond the Connection lookup: no client, no secret, no remote call.
    /// </summary>
    ValueTask<ConnectionToolSet?> GetToolsAsync(ConnectionId id, CancellationToken cancellationToken = default);
}
