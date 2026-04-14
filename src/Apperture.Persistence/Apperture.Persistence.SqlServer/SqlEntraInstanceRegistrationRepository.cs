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
        var e = new EntraInstanceRegistrationEntity
        {
            Id = string.IsNullOrWhiteSpace(registration.Id) ? Guid.NewGuid().ToString("N") : registration.Id,
            DisplayName = registration.DisplayName,
            Authority = registration.Authority,
            ClientId = registration.ClientId,
            UsesDeploymentSecret = registration.UsesDeploymentSecret,
            CallbackPath = registration.CallbackPath,
            Enabled = registration.Enabled,
            IsActive = registration.IsActive,
            CreatedUtc = now,
            UpdatedUtc = now
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
        e.CallbackPath = registration.CallbackPath;
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

    private static EntraInstanceRegistration ToDomain(EntraInstanceRegistrationEntity e) =>
        new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Authority = e.Authority,
            ClientId = e.ClientId,
            UsesDeploymentSecret = e.UsesDeploymentSecret,
            CallbackPath = e.CallbackPath,
            Enabled = e.Enabled,
            IsActive = e.IsActive,
            RowVersion = e.RowVersion is { Length: > 0 } ? e.RowVersion.ToArray() : null,
            CreatedUtc = e.CreatedUtc,
            UpdatedUtc = e.UpdatedUtc
        };
}
