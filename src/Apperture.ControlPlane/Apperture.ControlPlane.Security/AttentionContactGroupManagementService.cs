using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class AttentionContactGroupManagementService : IAttentionContactGroupManagementService
{
    private readonly AppertureDbContext _db;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly TimeProvider _time;

    public AttentionContactGroupManagementService(
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

    public async ValueTask<IReadOnlyList<AttentionContactGroupListItem>> ListAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        var q = _db.AttentionContactGroups.AsNoTracking();
        if (!includeDisabled)
            q = q.Where(x => x.IsEnabled);
        var rows = await q.OrderBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(static x => new AttentionContactGroupListItem(
            x.Id,
            x.Name,
            x.Description,
            x.IsEnabled,
            x.CreatedAtUtc,
            x.UpdatedAtUtc)).ToList();
    }

    public async ValueTask<AttentionContactGroupDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var g = await _db.AttentionContactGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (g is null)
            return null;
        var members = await _db.AttentionContactGroupMembers.AsNoTracking()
            .Where(m => m.AttentionContactGroupId == g.Id)
            .OrderBy(m => m.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(g, members);
    }

    public async ValueTask<AttentionContactGroupDetail> CreateAsync(
        AttentionContactGroupCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        var name = command.Name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Group name is required.");
        var now = _time.GetUtcNow();
        var entity = new AttentionContactGroupEntity
        {
            Id = NewGroupId(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            IsEnabled = command.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.AttentionContactGroups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDetail(entity, Array.Empty<AttentionContactGroupMemberEntity>());
    }

    public async ValueTask<AttentionContactGroupDetail?> UpdateAsync(
        string id,
        AttentionContactGroupUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var g = await _db.AttentionContactGroups
            .FirstOrDefaultAsync(x => x.Id == id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (g is null)
            return null;
        var name = command.Name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Group name is required.");
        g.Name = name;
        g.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        g.IsEnabled = command.IsEnabled;
        g.UpdatedAtUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var members = await _db.AttentionContactGroupMembers.AsNoTracking()
            .Where(m => m.AttentionContactGroupId == g.Id)
            .OrderBy(m => m.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(g, members);
    }

    public async ValueTask<AttentionContactGroupDetail?> ReplaceMembersAsync(
        string id,
        IReadOnlyList<AttentionContactGroupMemberUpsertItem> members,
        CancellationToken cancellationToken = default)
    {
        await RequireScheduleManageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var gid = id.Trim();
        var g = await _db.AttentionContactGroups
            .FirstOrDefaultAsync(x => x.Id == gid, cancellationToken)
            .ConfigureAwait(false);
        if (g is null)
            return null;

        var normalized = new List<(string Email, string? Display, bool Enabled)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in members)
        {
            var email = m.EmailAddress.Trim();
            if (email.Length == 0)
                throw new InvalidOperationException("Member email is required.");
            if (!seen.Add(email))
                throw new InvalidOperationException($"Duplicate email in request: {email}");
            normalized.Add((email, string.IsNullOrWhiteSpace(m.DisplayName) ? null : m.DisplayName.Trim(), m.IsEnabled));
        }

        var now = _time.GetUtcNow();
        var existing = await _db.AttentionContactGroupMembers
            .Where(x => x.AttentionContactGroupId == gid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.AttentionContactGroupMembers.RemoveRange(existing);

        foreach (var (email, display, enabled) in normalized)
        {
            _db.AttentionContactGroupMembers.Add(new AttentionContactGroupMemberEntity
            {
                Id = NewMemberId(),
                AttentionContactGroupId = gid,
                EmailAddress = email,
                DisplayName = display,
                IsEnabled = enabled,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        g.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var refreshed = await _db.AttentionContactGroupMembers.AsNoTracking()
            .Where(m => m.AttentionContactGroupId == gid)
            .OrderBy(m => m.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDetail(g, refreshed);
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
            throw new UnauthorizedAccessException("Not authorized to manage attention contact groups.");
    }

    private static AttentionContactGroupDetail ToDetail(
        AttentionContactGroupEntity g,
        IReadOnlyList<AttentionContactGroupMemberEntity> members) =>
        new(
            g.Id,
            g.Name,
            g.Description,
            g.IsEnabled,
            g.CreatedAtUtc,
            g.UpdatedAtUtc,
            members.Select(m => new AttentionContactGroupMemberItem(
                m.Id,
                m.EmailAddress,
                m.DisplayName,
                m.IsEnabled,
                m.CreatedAtUtc,
                m.UpdatedAtUtc)).ToList());

    private static string NewGroupId() => $"attncg_{Guid.NewGuid():N}";
    private static string NewMemberId() => $"attnmem_{Guid.NewGuid():N}";
}
