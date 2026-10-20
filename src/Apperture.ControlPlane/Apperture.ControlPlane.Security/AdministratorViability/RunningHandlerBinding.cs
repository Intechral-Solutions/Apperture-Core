namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>What is known about the sign-in handler the running host is using for Entra.</summary>
public enum RunningHandlerState
{
    /// <summary>
    /// The handler's configuration cannot be established: its options could not be built, or this process has no way to read
    /// them. It is never read as usable.
    /// </summary>
    Unavailable,

    /// <summary>The handler holds the disabled placeholders: Entra is off in the running host, and no Entra sign-in is possible.</summary>
    Disabled,

    /// <summary>The handler is bound to a real authority, client and secret.</summary>
    Bound,
}

/// <summary>
/// The configuration of the running host's OpenID Connect handler, as the handler itself holds it. The secret is reported only as
/// present or absent, never as a value.
/// </summary>
/// <param name="State">What is known.</param>
/// <param name="Authority">The authority the handler was built with (already carrying the tenant it will sign users in through).</param>
/// <param name="ClientId">The client id the handler was built with.</param>
/// <param name="HasClientSecret">The handler holds a client secret.</param>
/// <param name="Detail">Why the binding is unavailable or disabled, for the operator.</param>
public sealed record RunningHandlerBinding(
    RunningHandlerState State,
    string? Authority,
    string? ClientId,
    bool HasClientSecret,
    string? Detail)
{
    /// <summary>The binding of a handler that cannot be read.</summary>
    public static RunningHandlerBinding Unavailable(string detail) => new(RunningHandlerState.Unavailable, null, null, false, detail);

    /// <summary>The binding of a handler that holds the disabled placeholders.</summary>
    public static RunningHandlerBinding Disabled() => new(RunningHandlerState.Disabled, null, null, false, null);

    /// <summary>A handler bound to the given authority and client.</summary>
    public static RunningHandlerBinding Bound(string? authority, string? clientId, bool hasClientSecret) =>
        new(RunningHandlerState.Bound, authority, clientId, hasClientSecret, null);

    /// <summary>The handler can complete an Entra sign-in: it is bound, and has an authority, a client and a secret.</summary>
    public bool IsUsable =>
        State == RunningHandlerState.Bound
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && HasClientSecret;
}

/// <summary>
/// The seam to the running handler. The Management Host reads the options its OpenID Connect handler is actually given; the
/// framework keeps those in its own cache, built once from the Entra options of that moment and rebuilt only when the framework
/// invalidates them, so they are the one authority on what the handler will do. This is not a second copy of the sign-in
/// configuration.
/// </summary>
public interface IRunningEntraHandlerBindingSource
{
    /// <summary>Reads the binding now. A handler whose options cannot be read is reported as unavailable, never as usable.</summary>
    RunningHandlerBinding Read();
}
