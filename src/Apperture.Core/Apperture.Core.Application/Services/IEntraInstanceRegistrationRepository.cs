namespace Apperture.Core.Application.Services;

public interface IEntraInstanceRegistrationRepository
{
    Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default);

    Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default);

    Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Marks the given row active and clears <see cref="EntraInstanceRegistration.IsActive"/> on all others.</summary>
    Task SetActiveAsync(string id, CancellationToken cancellationToken = default);

    Task UpdateOpenIdValidationOutcomeAsync(
        string id,
        OpenIdConfigurationStatus status,
        DateTimeOffset validatedUtc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default);

    Task UpdateDirectoryDiscoveryOutcomeAsync(
        string id,
        DirectoryDiscoveryStatus status,
        DateTimeOffset utc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default);
}
