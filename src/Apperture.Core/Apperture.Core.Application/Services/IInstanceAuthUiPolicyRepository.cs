namespace Apperture.Core.Application.Services;

/// <summary>Persisted instance policy for management UI sign-in chooser (single row, Id = <c>instance</c>).</summary>
public sealed class InstanceAuthUiPolicy
{
    public bool ShowLocalSignInInPrimaryChooser { get; set; } = true;

    public bool PreferEntraInChooser { get; set; }

    public bool AutoRedirectToEntra { get; set; }

    /// <summary>Concurrency token from persistence; null when not loaded from store.</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>Reads and updates <see cref="InstanceAuthUiPolicy"/>.</summary>
public interface IInstanceAuthUiPolicyRepository
{
    Task<InstanceAuthUiPolicy> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists policy; throws if <paramref name="expectedRowVersion"/> does not match (optimistic concurrency).</summary>
    Task SaveAsync(InstanceAuthUiPolicy policy, byte[]? expectedRowVersion, CancellationToken cancellationToken = default);
}
