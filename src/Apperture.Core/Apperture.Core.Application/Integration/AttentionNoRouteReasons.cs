namespace Apperture.Core.Application.Integration;

/// <summary>Why a routing role (primary/escalation) could not produce recipients.</summary>
public static class AttentionNoRouteReasons
{
    public const string NoGroupConfigured = "no_group_configured";
    public const string GroupDisabled = "group_disabled";
    public const string GroupEmpty = "group_empty";
}
