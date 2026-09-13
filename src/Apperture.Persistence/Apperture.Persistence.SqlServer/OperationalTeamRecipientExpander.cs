using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed record OperationalTeamExpandedMember(
    string? UserId,
    string? ManagedContactId,
    string? Email,
    string? DisplayName,
    string MemberKind,
    string Usability);

public sealed record OperationalTeamRecipientExpansion(
    string? TeamId,
    string? TeamName,
    IReadOnlyList<OperationalTeamExpandedMember> Members,
    string? NoRouteReason);

public sealed class OperationalTeamRecipientExpander
{
    private const string MemberKindPrincipal = "Principal";
    private const string MemberKindExternalEmail = "ExternalEmail";
    private const string MemberKindManagedContact = "ManagedContact";

    private readonly AppertureDbContext _db;

    public OperationalTeamRecipientExpander(AppertureDbContext db) => _db = db;

    public async ValueTask<OperationalTeamRecipientExpansion> ExpandAsync(
        string? teamId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return new OperationalTeamRecipientExpansion(null, null, Array.Empty<OperationalTeamExpandedMember>(), null);

        var team = await _db.OperationalTeams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
            return new OperationalTeamRecipientExpansion(teamId, null, Array.Empty<OperationalTeamExpandedMember>(), "NoTeamConfigured");
        if (!team.IsEnabled)
            return new OperationalTeamRecipientExpansion(team.Id, team.Name, Array.Empty<OperationalTeamExpandedMember>(), "TeamDisabled");

        var members = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(m => m.OperationalTeamId == team.Id && m.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
            return new OperationalTeamRecipientExpansion(team.Id, team.Name, Array.Empty<OperationalTeamExpandedMember>(), "TeamEmpty");

        var expanded = new List<OperationalTeamExpandedMember>();
        var principalIds = members
            .Where(m => string.Equals(m.MemberKind, MemberKindPrincipal, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.PrincipalUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (principalIds.Length > 0)
        {
            var users = await _db.Users.AsNoTracking()
                .Where(u => principalIds.Contains(u.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var user in users)
            {
                var usability = string.IsNullOrWhiteSpace(user.EmailAddress)
                    ? NotificationUsabilityStatusValues.MissingEmail
                    : NotificationUsabilityStatusValues.Deliverable;
                expanded.Add(new OperationalTeamExpandedMember(
                    user.Id,
                    null,
                    user.EmailAddress?.Trim(),
                    user.DisplayName,
                    MemberKindPrincipal,
                    usability));
            }
        }

        foreach (var member in members.Where(m =>
                     string.Equals(m.MemberKind, MemberKindExternalEmail, StringComparison.OrdinalIgnoreCase)))
        {
            var email = member.EmailAddress?.Trim();
            var usability = string.IsNullOrWhiteSpace(email)
                ? NotificationUsabilityStatusValues.MissingEmail
                : NotificationUsabilityStatusValues.Deliverable;
            expanded.Add(new OperationalTeamExpandedMember(
                null,
                null,
                email,
                email,
                MemberKindExternalEmail,
                usability));
        }

        var managedContactIds = members
            .Where(m => string.Equals(m.MemberKind, MemberKindManagedContact, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.ManagedContactId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (managedContactIds.Length > 0)
        {
            var contacts = await _db.ManagedContacts.AsNoTracking()
                .Where(c => managedContactIds.Contains(c.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var contact in contacts)
            {
                var usability = !contact.IsEnabled
                    ? NotificationUsabilityStatusValues.ContactDisabled
                    : string.IsNullOrWhiteSpace(contact.EmailAddress)
                        ? NotificationUsabilityStatusValues.MissingEmail
                        : NotificationUsabilityStatusValues.Deliverable;
                expanded.Add(new OperationalTeamExpandedMember(
                    contact.LinkedUserId,
                    contact.Id,
                    contact.EmailAddress?.Trim(),
                    contact.DisplayName,
                    MemberKindManagedContact,
                    usability));
            }
        }

        return new OperationalTeamRecipientExpansion(team.Id, team.Name, expanded, null);
    }

    public async ValueTask<string[]> ExpandToEmailAddressesAsync(
        string? teamId,
        CancellationToken cancellationToken = default)
    {
        var expansion = await ExpandAsync(teamId, cancellationToken).ConfigureAwait(false);
        return expansion.Members
            .Where(m => m.Usability == NotificationUsabilityStatusValues.Deliverable && !string.IsNullOrWhiteSpace(m.Email))
            .Select(m => m.Email!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
