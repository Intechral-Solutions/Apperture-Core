namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to update a connection (partial update). There is no way to name a secret reference: a client can neither read one
/// (responses carry only whether a field is configured) nor write one.
/// </summary>
/// <param name="SecretValues">
/// Optional plaintext values for declared secret fields. Each is stored as a new active version of the secret the field already
/// owns, or, when the field names nothing, or names a secret it does not own (another connection's, another provider's, a missing
/// or unreadable one), as a new secret the connection owns that the field is repointed to, atomically with the rest of the update.
/// The secret the field named before is never read, adopted, rotated, disabled or otherwise changed.
/// </param>
/// <param name="ClearSecretFields">
/// Optional keys of secret fields whose stored reference is removed. Any stored key is accepted, a field the connector no longer
/// declares included. Clearing never touches the secret the reference named. A field cannot be cleared and given a value in the
/// same request.
/// </param>
public sealed record UpdateConnectionRequest(
    string? Name = null,
    IReadOnlyDictionary<string, string>? Config = null,
    IReadOnlyDictionary<string, string>? SecretValues = null,
    IReadOnlyList<string>? ClearSecretFields = null);

/// <summary>
/// A connection request that is malformed in a way the caller can fix, such as clearing and setting the same field, or clearing a
/// field the connection does not have. It is an <see cref="InvalidOperationException"/> so existing handlers still see a refusal,
/// and a distinct type so the API can answer 400 instead of treating it like a missing connection. The message names fields by
/// key only and never repeats a reference.
/// </summary>
public sealed class InvalidConnectionRequestException(string message) : InvalidOperationException(message);
