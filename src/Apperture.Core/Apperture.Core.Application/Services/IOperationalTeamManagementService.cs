namespace Apperture.Core.Application.Services;

public static class OperationalTeamMemberKinds
{
    public const string Principal = "Principal";
    public const string ExternalEmail = "ExternalEmail";
    public const string ManagedContact = "ManagedContact";
}

public sealed record OperationalTeamListItem(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationalTeamMemberItem(
    string Id,
    string MemberKind,
    string? PrincipalUserId,
    string? EmailAddress,
    string? ManagedContactId,
    string? DisplayName,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationalTeamDetail(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<OperationalTeamMemberItem> Members);

public sealed record OperationalTeamCreateCommand(string Name, string? Description, bool IsEnabled);
public sealed record OperationalTeamUpdateCommand(string Name, string? Description, bool IsEnabled);

public sealed record OperationalTeamMemberUpsertItem(
    string? Id,
    string MemberKind,
    string? PrincipalUserId,
    string? EmailAddress,
    string? DisplayName,
    bool IsEnabled,
    string? ManagedContactId = null);

public interface IOperationalTeamManagementService
{
    ValueTask<IReadOnlyList<OperationalTeamListItem>> ListAsync(bool includeDisabled, CancellationToken cancellationToken = default);
    ValueTask<OperationalTeamDetail?> GetAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<OperationalTeamDetail> CreateAsync(OperationalTeamCreateCommand command, CancellationToken cancellationToken = default);
    ValueTask<OperationalTeamDetail?> UpdateAsync(string id, OperationalTeamUpdateCommand command, CancellationToken cancellationToken = default);
    ValueTask<OperationalTeamDetail?> ReplaceMembersAsync(string id, IReadOnlyList<OperationalTeamMemberUpsertItem> members, CancellationToken cancellationToken = default);
}
