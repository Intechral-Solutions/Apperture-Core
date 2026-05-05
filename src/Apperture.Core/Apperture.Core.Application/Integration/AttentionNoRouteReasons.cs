namespace Apperture.Core.Application.Integration;

/// <summary>Why a routing role (primary/escalation) could not produce recipients.</summary>
public static class AttentionNoRouteReasons
{
    public const string NoTeamConfigured = "no_team_configured";
    public const string TeamDisabled = "team_disabled";
    public const string TeamEmpty = "team_empty";
    public const string PrincipalMembersNoEmail = "principal_members_no_email";

    /// <summary>Managed-contact members exist, but none has a deliverable email (team has no principal/external members).</summary>
    public const string ManagedContactMembersNoDeliverableEmail = "managed_contact_members_no_deliverable_email";

    /// <summary>Multiple member kinds and/or external addresses contributed nothing deliverable (mixed failure).</summary>
    public const string MemberRecipientsNoDeliverableEmail = "member_recipients_no_deliverable_email";

    // Back-compat aliases from Epic 71 terminology.
    public const string NoGroupConfigured = NoTeamConfigured;
    public const string GroupDisabled = TeamDisabled;
    public const string GroupEmpty = TeamEmpty;
}
