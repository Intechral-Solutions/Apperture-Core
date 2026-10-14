using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Connector;

/// <summary>Which kind of operation asked for a connection's material. It is recorded on a refusal and decides the audit actor.</summary>
public enum ConnectionResolutionPath
{
    /// <summary>A workflow step resolving a bound slot in the Worker Host. The actor is the worker.</summary>
    Run,

    /// <summary>A test-connect (a connection verifier). The actor is the operator who asked.</summary>
    Verify,

    /// <summary>A structure browse. The actor is the operator who asked.</summary>
    Browse,

    /// <summary>Integration setting options that need a live client (the Campus Nexus campuses). The actor is the operator who asked.</summary>
    SettingsOptions,
}

/// <summary>
/// The only way to turn a <see cref="Connection"/> into <see cref="ResolvedConnectionMaterial"/>. Every secret field of the
/// connection is resolved through <c>ISecretResolver.ResolveOwnedAsync</c> against the owner the connection's own field defines
/// (<see cref="ConnectionSecretOwnership"/>), so a reference stored on a connection row that names another holder's secret is
/// refused before that secret is read. Worker slot resolution, test-connect for each connector, structure browse and the
/// integration settings options all come here; a new consumer of <c>Connection.SecretRefs</c> must too (an architecture test
/// fails otherwise).
/// </summary>
public interface IConnectionMaterialResolver
{
    /// <summary>
    /// Merges the connection's config with every resolved secret field. One call is one top-level operation: the first field
    /// that cannot be resolved aborts it, one refusal is recorded for the whole call, and no material is returned.
    /// </summary>
    /// <exception cref="ConnectionSecretUnavailableException">
    /// A secret field is blank, unreadable, missing, disabled or not owned by this connection field. The refusal is the same
    /// whichever it was, and the secret is never delivered whether or not the evidence of the refusal could be written.
    /// </exception>
    ValueTask<ResolvedConnectionMaterial> ResolveAsync(
        Connection connection,
        ConnectionResolutionPath path,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The typed refusal of <see cref="IConnectionMaterialResolver"/>. The message is fixed. The field key is the only thing it names:
/// never the reference, the secret that was found, its owner or which condition failed.
/// </summary>
public sealed class ConnectionSecretUnavailableException : InvalidOperationException
{
    public const string FixedMessage = "A secret this connection needs could not be resolved.";

    public ConnectionSecretUnavailableException(string fieldKey)
        : base(FixedMessage)
    {
        FieldKey = fieldKey;
    }

    /// <summary>The stored key of the first field that was refused.</summary>
    public string FieldKey { get; }
}
