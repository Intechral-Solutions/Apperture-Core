using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlEntraInstanceRegistrationRepository : IEntraInstanceRegistrationRepository
{
    private readonly AppertureDbContext _db;

    public SqlEntraInstanceRegistrationRepository(AppertureDbContext db) => _db = db;

    public async Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.EntraInstanceRegistrations.AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return list.ConvertAll(ToDomain);
    }

    public async Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var openIdStatus = registration.OpenIdConfigurationStatus == OpenIdConfigurationStatus.Unknown
            ? OpenIdConfigurationStatus.NotValidated
            : registration.OpenIdConfigurationStatus;
        var dirStatus = registration.DirectoryDiscoveryStatus == DirectoryDiscoveryStatus.Unknown
            ? DirectoryDiscoveryStatus.NotAttempted
            : registration.DirectoryDiscoveryStatus;

        var e = new EntraInstanceRegistrationEntity
        {
            Id = string.IsNullOrWhiteSpace(registration.Id) ? Guid.NewGuid().ToString("N") : registration.Id,
            DisplayName = registration.DisplayName,
            Authority = registration.Authority,
            ClientId = registration.ClientId,
            UsesDeploymentSecret = registration.UsesDeploymentSecret,
            ClientSecretRef = registration.ClientSecretRef,
            CallbackPath = registration.CallbackPath,
            Enabled = registration.Enabled,
            IsActive = registration.IsActive,
            CreatedUtc = now,
            UpdatedUtc = now,
            AzureTenantId = registration.AzureTenantId,
            AuthenticationContextId = registration.AuthenticationContextId,
            OpenIdConfigurationStatus = (int)openIdStatus,
            LastOpenIdValidationUtc = registration.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = registration.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = registration.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = (int)dirStatus,
            LastDirectoryDiscoveryUtc = registration.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = registration.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = registration.LastDirectoryDiscoveryDetail
        };
        _db.EntraInstanceRegistrations.Add(e);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDomain(e);
    }

    public async Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == registration.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Entra registration '{registration.Id}' was not found.");

        if (expectedRowVersion is not null && (e.RowVersion is null || !e.RowVersion.SequenceEqual(expectedRowVersion)))
            throw new DbUpdateConcurrencyException("Entra registration was modified by another request.");

        e.DisplayName = registration.DisplayName;
        e.Authority = registration.Authority;
        e.ClientId = registration.ClientId;
        e.UsesDeploymentSecret = registration.UsesDeploymentSecret;
        e.ClientSecretRef = registration.ClientSecretRef;
        e.CallbackPath = registration.CallbackPath;
        e.AzureTenantId = string.IsNullOrWhiteSpace(registration.AzureTenantId)
            ? null
            : registration.AzureTenantId.Trim();
        e.AuthenticationContextId = string.IsNullOrWhiteSpace(registration.AuthenticationContextId)
            ? null
            : registration.AuthenticationContextId.Trim();
        e.Enabled = registration.Enabled;
        if (!registration.Enabled && e.IsActive)
            e.IsActive = false;
        e.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
            return;
        _db.EntraInstanceRegistrations.Remove(e);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(string id, CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await _db.EntraInstanceRegistrations.ToListAsync(cancellationToken);
            var target = rows.FirstOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException($"Entra registration '{id}' was not found.");
            if (!target.Enabled)
                throw new InvalidOperationException("Cannot activate a disabled registration.");

            var now = DateTimeOffset.UtcNow;
            foreach (var r in rows)
            {
                r.IsActive = r.Id == id;
                r.UpdatedUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }

    public async Task UpdateOpenIdValidationOutcomeAsync(
        string id,
        OpenIdConfigurationStatus status,
        DateTimeOffset validatedUtc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
            return;

        e.OpenIdConfigurationStatus = (int)status;
        e.LastOpenIdValidationUtc = validatedUtc;
        e.LastOpenIdValidationSummary = summary;
        e.LastOpenIdValidationDetail = detail;
        e.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDirectoryDiscoveryOutcomeAsync(
        string id,
        DirectoryDiscoveryStatus status,
        DateTimeOffset utc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
            return;

        e.DirectoryDiscoveryStatus = (int)status;
        e.LastDirectoryDiscoveryUtc = utc;
        e.LastDirectoryDiscoverySummary = summary;
        e.LastDirectoryDiscoveryDetail = detail;
        e.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static EntraInstanceRegistration ToDomain(EntraInstanceRegistrationEntity e) =>
        new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Authority = e.Authority,
            ClientId = e.ClientId,
            UsesDeploymentSecret = e.UsesDeploymentSecret,
            ClientSecretRef = e.ClientSecretRef,
            CallbackPath = e.CallbackPath,
            Enabled = e.Enabled,
            IsActive = e.IsActive,
            RowVersion = e.RowVersion is { Length: > 0 } ? e.RowVersion.ToArray() : null,
            CreatedUtc = e.CreatedUtc,
            UpdatedUtc = e.UpdatedUtc,
            AzureTenantId = e.AzureTenantId,
            AuthenticationContextId = e.AuthenticationContextId,
            OpenIdConfigurationStatus = (OpenIdConfigurationStatus)e.OpenIdConfigurationStatus,
            LastOpenIdValidationUtc = e.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = e.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = e.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = (DirectoryDiscoveryStatus)e.DirectoryDiscoveryStatus,
            LastDirectoryDiscoveryUtc = e.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = e.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = e.LastDirectoryDiscoveryDetail
        };
}
