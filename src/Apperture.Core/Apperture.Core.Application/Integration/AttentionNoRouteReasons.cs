namespace Apperture.Core.Application.Integration;

/// <summary>Why a routing role (primary/escalation) could not produce recipients.</summary>
public static class AttentionNoRouteReasons
{
    public const string NoTeamConfigured = "no_team_configured";
    public const string TeamDisabled = "team_disabled";
    public const string TeamEmpty = "team_empty";
    public const string PrincipalMembersNoEmail = "principal_members_no_email";

    // Back-compat aliases from Epic 71 terminology.
    public const string NoGroupConfigured = NoTeamConfigured;
    public const string GroupDisabled = TeamDisabled;
    public const string GroupEmpty = TeamEmpty;
}
