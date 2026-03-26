using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class OperationalTeamManagementService : IOperationalTeamManagementService
{
    private readonly AppertureDbContext _db;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly TimeProvider _time;

    public OperationalTeamManagementService(
        AppertureDbContext db,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        TimeProvider time)
    {
        _db = db;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _time = time;
    }

    public async ValueTask<IReadOnlyList<OperationalTeamListItem>> ListAsync(bool includeDisabled, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        var q = _db.OperationalTeams.AsNoTracking();
        if (!includeDisabled)
            q = q.Where(x => x.IsEnabled);
        var rows = await q.OrderBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(static x => new OperationalTeamListItem(x.Id, x.Name, x.Description, x.IsEnabled, x.CreatedAtUtc, x.UpdatedAtUtc)).ToList();
    }

    public async ValueTask<OperationalTeamDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var team = await _db.OperationalTeams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
            return null;
        var members = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(x => x.OperationalTeamId == team.Id)
            .OrderBy(x => x.MemberKind)
            .ThenBy(x => x.EmailAddress)
            .ThenBy(x => x.PrincipalUserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(team, members);
    }

    public async ValueTask<OperationalTeamDetail> CreateAsync(OperationalTeamCreateCommand command, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        var name = command.Name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Team name is required.");
        var now = _time.GetUtcNow();
        var row = new OperationalTeamEntity
        {
            Id = $"opteam_{Guid.NewGuid():N}",
            Name = name,
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            IsEnabled = command.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.OperationalTeams.Add(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDetail(row, Array.Empty<OperationalTeamMemberEntity>());
    }

    public async ValueTask<OperationalTeamDetail?> UpdateAsync(string id, OperationalTeamUpdateCommand command, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var row = await _db.OperationalTeams.FirstOrDefaultAsync(x => x.Id == id.Trim(), cancellationToken).ConfigureAwait(false);
        if (row is null)
            return null;
        row.Name = command.Name.Trim();
        row.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        row.IsEnabled = command.IsEnabled;
        row.UpdatedAtUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var members = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(x => x.OperationalTeamId == row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(row, members);
    }

    public async ValueTask<OperationalTeamDetail?> ReplaceMembersAsync(string id, IReadOnlyList<OperationalTeamMemberUpsertItem> members, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var teamId = id.Trim();
        var team = await _db.OperationalTeams.FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken).ConfigureAwait(false);
        if (team is null)
            return null;

        var now = _time.GetUtcNow();
        var existing = await _db.OperationalTeamMembers.Where(x => x.OperationalTeamId == teamId).ToListAsync(cancellationToken).ConfigureAwait(false);
        _db.OperationalTeamMembers.RemoveRange(existing);

        foreach (var m in members)
        {
            if (m.MemberKind == OperationalTeamMemberKinds.Principal)
            {
                if (string.IsNullOrWhiteSpace(m.PrincipalUserId))
                    throw new InvalidOperationException("Principal member requires PrincipalUserId.");
            }
            else if (m.MemberKind == OperationalTeamMemberKinds.ExternalEmail)
            {
                if (string.IsNullOrWhiteSpace(m.EmailAddress))
                    throw new InvalidOperationException("ExternalEmail member requires EmailAddress.");
            }
            else
            {
                throw new InvalidOperationException($"Unsupported member kind '{m.MemberKind}'.");
            }

            _db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
            {
                Id = $"opteammem_{Guid.NewGuid():N}",
                OperationalTeamId = teamId,
                MemberKind = m.MemberKind,
                PrincipalUserId = string.IsNullOrWhiteSpace(m.PrincipalUserId) ? null : m.PrincipalUserId.Trim(),
                EmailAddress = string.IsNullOrWhiteSpace(m.EmailAddress) ? null : m.EmailAddress.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? null : m.DisplayName.Trim(),
                IsEnabled = m.IsEnabled,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        team.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(x => x.OperationalTeamId == teamId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(team, refreshed);
    }

    private async ValueTask RequireScheduleManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(
                    ActionConstants.ScheduleManage,
                    new ResourceDescriptor(ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage operational teams.");
    }

    private static OperationalTeamDetail ToDetail(OperationalTeamEntity team, IReadOnlyList<OperationalTeamMemberEntity> members) =>
        new(
            team.Id,
            team.Name,
            team.Description,
            team.IsEnabled,
            team.CreatedAtUtc,
            team.UpdatedAtUtc,
            members.Select(x => new OperationalTeamMemberItem(
                x.Id,
                x.MemberKind,
                x.PrincipalUserId,
                x.EmailAddress,
                x.DisplayName,
                x.IsEnabled,
                x.CreatedAtUtc,
                x.UpdatedAtUtc)).ToList());
}
