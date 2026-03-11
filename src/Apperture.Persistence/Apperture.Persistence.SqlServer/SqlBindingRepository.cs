using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
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
    public async ValueTask<Binding?> GetBySlotAsync(string moduleId, string slotName, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Bindings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ModuleId == moduleId && e.SlotName == slotName, cancellationToken);
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
        e.ModuleId,
        e.SlotName,
        e.ContractType,
        e.ConnectionId is not null ? new ConnectionId(e.ConnectionId) : null,
        e.CapabilityId,
        (BindingState)e.State);

    private static BindingEntity ToEntity(Binding b) => new()
    {
        Id = b.Id.Value,
        ModuleId = b.ModuleId,
        SlotName = b.SlotName,
        ContractType = b.ContractType,
        ConnectionId = b.ConnectionId?.Value,
        CapabilityId = b.CapabilityId,
        State = (int)b.State
    };
}
