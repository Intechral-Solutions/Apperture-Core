namespace Apperture.Management.Ui.Shared;

public static class NotificationRecipientSourceLabels
{
    public static string Label(string? source) => source switch
    {
        "attentionRoutingIntegrationOverride" => "Implicit (attention routing compatibility)",
        "directSubscription" => "Explicit subscription",
        "operationalTeamMembership" => "Operational team membership",
        _ => source ?? "—"
    };

    public static bool IsImplicitAttentionRouting(string? source) =>
        string.Equals(source, "attentionRoutingIntegrationOverride", StringComparison.Ordinal);
}
