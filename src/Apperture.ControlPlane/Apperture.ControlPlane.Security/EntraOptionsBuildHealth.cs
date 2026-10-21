using System.Runtime.CompilerServices;

namespace Apperture.ControlPlane.Security;

/// <summary>How completely a set of Entra options was built from the persisted registration and its secret.</summary>
public enum EntraOptionsBuildQuality
{
    /// <summary>Everything the build needed was read: the persisted registration (or the absence of one) and its secret (or its confirmed absence).</summary>
    Complete,

    /// <summary>
    /// The build could not read the persisted registration or its client secret. The options were built from the deployment
    /// configuration alone, as they always have been in that case, and the framework keeps them until it rebuilds them. They are
    /// what the host runs, and they may not be what the persisted configuration says.
    /// </summary>
    Degraded,
}

/// <summary>
/// A record of how each set of Entra options was built (IAM-10.2b WP3, R-1). The options are built once, on first use, and cached;
/// a build that could not read the registration store or the secret store leaves them in a state the persisted configuration does
/// not describe, silently, until the framework rebuilds them. This records that fact against the options instance the build
/// produced, so the viability reading can say it does not know instead of describing a frozen, incomplete configuration as
/// settled. It holds no configuration value and no secret: it is not a copy of the authentication configuration, only a note about
/// one, and it forgets an options instance when that instance is collected.
/// </summary>
public interface IEntraOptionsBuildHealth
{
    /// <summary>Notes how <paramref name="options"/> was built. <paramref name="reasonCode"/> is a fixed code, never an error message.</summary>
    void Record(object options, EntraOptionsBuildQuality quality, string? reasonCode = null);

    /// <summary>How <paramref name="options"/> was built, or null when nobody recorded it.</summary>
    EntraOptionsBuildQuality? QualityOf(object options);

    /// <summary>The fixed reason code recorded with a degraded build, or null.</summary>
    string? ReasonOf(object options);

    /// <summary>Records for <paramref name="to"/> whatever was recorded for <paramref name="from"/>, when it derives from it.</summary>
    void Carry(object from, object to);
}

/// <inheritdoc cref="IEntraOptionsBuildHealth" />
public sealed class EntraOptionsBuildHealth : IEntraOptionsBuildHealth
{
    /// <summary>The registration store could not be read.</summary>
    public const string RegistrationUnreadable = "registration_unreadable";

    /// <summary>The active registration names a stored client secret and the secret store could not be read.</summary>
    public const string ClientSecretUnavailable = "client_secret_unavailable";

    private sealed record Note(EntraOptionsBuildQuality Quality, string? Reason);

    private readonly ConditionalWeakTable<object, Note> _notes = new();

    /// <inheritdoc />
    public void Record(object options, EntraOptionsBuildQuality quality, string? reasonCode = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _notes.AddOrUpdate(options, new Note(quality, reasonCode));
    }

    /// <inheritdoc />
    public EntraOptionsBuildQuality? QualityOf(object options) =>
        options is not null && _notes.TryGetValue(options, out var note) ? note.Quality : null;

    /// <inheritdoc />
    public string? ReasonOf(object options) =>
        options is not null && _notes.TryGetValue(options, out var note) ? note.Reason : null;

    /// <inheritdoc />
    public void Carry(object from, object to)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (from is not null && _notes.TryGetValue(from, out var note))
            _notes.AddOrUpdate(to, note);
    }
}
