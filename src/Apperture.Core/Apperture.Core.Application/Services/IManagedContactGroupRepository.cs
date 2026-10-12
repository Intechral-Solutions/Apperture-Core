namespace Apperture.Core.Application.Services;

public interface IManagedContactGroupRepository
{
    Task<IReadOnlyList<ManagedContactGroupRepositoryRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The imported group row of this Entra group object id, or null.</summary>
    Task<ManagedContactGroupRepositoryRow?> FindByEntraGroupObjectIdAsync(
        string entraGroupObjectId,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts by Entra group object id (metadata only; no membership sync in Epic 76).</summary>
    Task<ManagedContactGroupRepositoryRow> UpsertEntraGroupAsync(
        string entraGroupObjectId,
        string displayName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedContactGroupRepositoryRow(
    string Id,
    string DisplayName,
    string Source,
    string EntraGroupObjectId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
