using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for binding resources.
/// </summary>
public sealed class SqlBindingRepository : IBindingRepository
{
    private readonly AppertureDbContext _db;

    public SqlBindingRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<Binding?> GetUnownedBySlotAsync(string moduleId, string slotName, CancellationToken cancellationToken = default)
    {
        // IntegrationId == null is load-bearing: it restricts this contextless lookup to genuinely
        // global bindings. Without it the query returns the first instance-wide match and one
        // Integration can silently resolve another Integration's Connection.
        var entity = await _db.Bindings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ModuleId == moduleId && e.SlotName == slotName && e.IntegrationId == null, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<Binding?> GetByIdAsync(BindingId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Bindings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<Binding?> GetByIdForUpdateAsync(BindingId id, CancellationToken cancellationToken = default)
    {
        List<BindingEntity> rows;
        if (_db.Database.IsRelational())
        {
            // AsNoTracking is load-bearing: a rerun of an execution-strategy attempt must read afresh. The lock hint lives in
            // the SQL text, so it is taken and held to the end of the transaction whatever the tracking behaviour.
            rows = await _db.Bindings
                .FromSqlInterpolated($"SELECT * FROM [Bindings] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await _db.Bindings.AsNoTracking().Where(e => e.Id == id.Value).ToListAsync(cancellationToken);
        }

        var entity = rows.SingleOrDefault();
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Binding>> GetByIntegrationIdAsync(IntegrationId integrationId, CancellationToken cancellationToken = default)
    {
        var entities = await _db.Bindings.AsNoTracking()
            .Where(e => e.IntegrationId == integrationId.Value)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<Binding?> GetBySlotAndIntegrationAsync(string moduleId, string slotName, IntegrationId integrationId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Bindings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ModuleId == moduleId && e.SlotName == slotName && e.IntegrationId == integrationId.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Binding>> GetByConnectionIdAsync(ConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        var entities = await _db.Bindings.AsNoTracking()
            .Where(e => e.ConnectionId == connectionId.Value)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(Binding binding, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(binding);
        _db.Bindings.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(Binding binding, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Bindings.FirstOrDefaultAsync(e => e.Id == binding.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Binding {binding.Id.Value} not found.");

        entity.State = (int)binding.State;
        entity.IntegrationId = binding.IntegrationId?.Value;
        entity.ConnectionId = binding.ConnectionId?.Value;
        entity.CapabilityId = binding.CapabilityId;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(BindingId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Bindings.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        if (entity is not null)
        {
            _db.Bindings.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static Binding ToDomain(BindingEntity e) => new(
        new BindingId(e.Id),
        e.IntegrationId is not null ? new IntegrationId(e.IntegrationId) : null,
        e.ModuleId,
        e.SlotName,
        e.ContractType,
        e.ConnectionId is not null ? new ConnectionId(e.ConnectionId) : null,
        e.CapabilityId,
        (BindingState)e.State);

    private static BindingEntity ToEntity(Binding b) => new()
    {
        Id = b.Id.Value,
        IntegrationId = b.IntegrationId?.Value,
        ModuleId = b.ModuleId,
        SlotName = b.SlotName,
        ContractType = b.ContractType,
        ConnectionId = b.ConnectionId?.Value,
        CapabilityId = b.CapabilityId,
        State = (int)b.State
    };
}
