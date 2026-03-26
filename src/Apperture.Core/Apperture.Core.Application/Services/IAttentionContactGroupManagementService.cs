namespace Apperture.Core.Application.Services;

public sealed record AttentionContactGroupListItem(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AttentionContactGroupMemberItem(
    string Id,
    string EmailAddress,
    string? DisplayName,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AttentionContactGroupDetail(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AttentionContactGroupMemberItem> Members);

public sealed record AttentionContactGroupCreateCommand(
    string Name,
    string? Description,
    bool IsEnabled);

public sealed record AttentionContactGroupUpdateCommand(
    string Name,
    string? Description,
    bool IsEnabled);

public sealed record AttentionContactGroupMemberUpsertItem(
    string? Id,
    string EmailAddress,
    string? DisplayName,
    bool IsEnabled);

public interface IAttentionContactGroupManagementService
{
    ValueTask<IReadOnlyList<AttentionContactGroupListItem>> ListAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default);

    ValueTask<AttentionContactGroupDetail?> GetAsync(string id, CancellationToken cancellationToken = default);

    ValueTask<AttentionContactGroupDetail> CreateAsync(
        AttentionContactGroupCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<AttentionContactGroupDetail?> UpdateAsync(
        string id,
        AttentionContactGroupUpdateCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the full member set for the group.</summary>
    ValueTask<AttentionContactGroupDetail?> ReplaceMembersAsync(
        string id,
        IReadOnlyList<AttentionContactGroupMemberUpsertItem> members,
        CancellationToken cancellationToken = default);
}
