namespace Apperture.Management.Client;

public sealed record OperationalTeamSummaryDto(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationalTeamMemberDto(
    string Id,
    string MemberKind,
    string? PrincipalUserId,
    string? EmailAddress,
    string? ManagedContactId,
    string? DisplayName,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationalTeamDetailDto(
    string Id,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<OperationalTeamMemberDto> Members);

public sealed record CreateOperationalTeamRequest(string Name, string? Description, bool IsEnabled);
public sealed record UpdateOperationalTeamRequest(string Name, string? Description, bool IsEnabled);
public sealed record OperationalTeamMemberInputDto(
    string? Id,
    string MemberKind,
    string? PrincipalUserId,
    string? EmailAddress,
    string? ManagedContactId,
    string? DisplayName,
    bool IsEnabled);
public sealed record ReplaceOperationalTeamMembersRequest(IReadOnlyList<OperationalTeamMemberInputDto> Members);
