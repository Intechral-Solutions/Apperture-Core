namespace Apperture.Management.Client;

public sealed record AttentionContactGroupSummaryDto(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AttentionContactGroupMemberDto(
    string Id,
    string EmailAddress,
    string? DisplayName,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AttentionContactGroupDetailDto(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AttentionContactGroupMemberDto> Members);

public sealed record CreateAttentionContactGroupRequest(string Name, string? Description, bool IsEnabled);

public sealed record UpdateAttentionContactGroupRequest(string Name, string? Description, bool IsEnabled);

public sealed record AttentionContactGroupMemberInputDto(string? Id, string EmailAddress, string? DisplayName, bool IsEnabled);

public sealed record ReplaceAttentionContactGroupMembersRequest(IReadOnlyList<AttentionContactGroupMemberInputDto> Members);
