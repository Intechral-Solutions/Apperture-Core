using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.ConnectorResolution;

/// <summary>
/// The connection-material boundary (<see cref="IConnectionMaterialResolver"/>). It is the only production code that reads
/// <c>Connection.SecretRefs</c> and hands a reference to <see cref="ISecretResolver"/>.
/// </summary>
/// <remarks>
/// <para><b>Ownership first.</b> Each field is resolved with <see cref="ISecretResolver.ResolveOwnedAsync"/> against
/// <see cref="ConnectionSecretOwnership.For"/>, so a reference that names another holder's secret is refused before that
/// secret's payload is read or unprotected. A blank reference, an empty key, an unreadable or missing reference, a disabled secret
/// and a foreign one are all the same refusal.</para>
/// <para><b>One refusal row per call.</b> A call is one top-level operation: the first refused field aborts it, one W3
/// <c>connection.secret.resolution_refused</c> row is written for it, and the wrapping layers (the slot resolver, the default
/// resolver, a verifier) write nothing of their own. The row is attributed to the worker on <see cref="ConnectionResolutionPath.Run"/>
/// and to the signed-in operator on every other path.</para>
/// <para><b>Evidence never gates the refusal.</b> The refusal is decided before the row is attempted. A row that cannot be
/// written (the writer reports it, or throws) is logged, and the caller still gets the refusal and no secret.</para>
/// </remarks>
public sealed class ConnectionMaterialResolver : IConnectionMaterialResolver
{
    /// <summary>How long a refusal's evidence write may take. It is bounded because the caller is waiting to be told no.</summary>
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(15);

    private readonly ISecretResolver _secretResolver;
    private readonly IAuditEventWriter _auditEvents;
    private readonly ILogger<ConnectionMaterialResolver> _logger;

    public ConnectionMaterialResolver(
        ISecretResolver secretResolver,
        IAuditEventWriter auditEvents,
        ILogger<ConnectionMaterialResolver>? logger = null)
    {
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _auditEvents = auditEvents ?? throw new ArgumentNullException(nameof(auditEvents));
        _logger = logger ?? NullLogger<ConnectionMaterialResolver>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<ResolvedConnectionMaterial> ResolveAsync(
        Connection connection,
        ConnectionResolutionPath path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var merged = new Dictionary<string, string>(connection.Config);

        // Ordinal key order, so the field a multi-field refusal names does not depend on dictionary enumeration order.
        foreach (var field in connection.SecretRefs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            ResolvedSecret resolved;
            try
            {
                if (string.IsNullOrEmpty(field.Key) || string.IsNullOrWhiteSpace(field.Value))
                    throw new SecretUnavailableException();

                resolved = await _secretResolver
                    .ResolveOwnedAsync(new SecretRef(field.Value), ConnectionSecretOwnership.For(connection.Id, field.Key), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SecretUnavailableException)
            {
                await RecordRefusalAsync(connection, field.Key, path).ConfigureAwait(false);
                throw new ConnectionSecretUnavailableException(field.Key);
            }

            merged[field.Key] = resolved.Material.OpaqueString ?? resolved.Material.Json ?? "";
        }

        return new ResolvedConnectionMaterial(merged);
    }

    /// <summary>
    /// Writes the W3 row. It never throws and its result decides nothing: the refusal was settled before this runs. What is
    /// logged is the connection id, the field key and the path; never the reference, which is not in scope here.
    /// </summary>
    private async Task RecordRefusalAsync(Connection connection, string fieldKey, ConnectionResolutionPath path)
    {
        _logger.LogWarning(
            "Connection {ConnectionId} was refused a secret for field {FieldKey} on path {Path}.",
            connection.Id.Value, fieldKey, PathName(path));
        try
        {
            using var timeout = new CancellationTokenSource(EvidenceTimeout);
            var written = await _auditEvents.TryWriteAsync(
                AuditEvents.ConnectionSecretResolutionRefused(
                    connection.Id.Value,
                    Bounded(fieldKey),
                    PathName(path),
                    connection.ResourceContextId.Value),
                path == ConnectionResolutionPath.Run ? AuditActor.Worker : AuditActor.CurrentPrincipal,
                timeout.Token).ConfigureAwait(false);
            if (!written)
                _logger.LogWarning("The refusal of connection {ConnectionId} was not recorded in the audit trail.", connection.Id.Value);
        }
        catch (Exception ex)
        {
            // The type only: a message is not needed to know evidence was lost, and none is trusted not to carry a value.
            _logger.LogWarning(
                "The refusal of connection {ConnectionId} could not be recorded in the audit trail ({ExceptionType}).",
                connection.Id.Value, ex.GetType().Name);
        }
    }

    /// <summary>A legacy key is arbitrary text; the row keeps the part the schema allows.</summary>
    private static string Bounded(string fieldKey) =>
        string.IsNullOrEmpty(fieldKey)
            ? "(blank)"
            : fieldKey.Length <= AuditDetailSchema.MaxIdLength ? fieldKey : fieldKey[..AuditDetailSchema.MaxIdLength];

    private static string PathName(ConnectionResolutionPath path) => path switch
    {
        ConnectionResolutionPath.Run => "run",
        ConnectionResolutionPath.Verify => "verify",
        ConnectionResolutionPath.Browse => "browse",
        ConnectionResolutionPath.SettingsOptions => "settings_options",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown resolution path."),
    };
}
