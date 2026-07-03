namespace Apperture.Core.Domain.Notifications;

public static class NotificationRecipientKinds
{
    public const string Principal = "principal";
    public const string OperationalTeam = "operationalTeam";
    public const string ManagedContact = "managedContact";
}

public static class NotificationDeliveryModes
{
    public const string Immediate = "immediate";
    public const string Muted = "muted";
    public const string DigestOnly = "digestOnly";
    public const string ImmediateAndDigest = "immediateAndDigest";
}

public static class NotificationSubscriptionOrigins
{
    public const string SystemAssigned = "systemAssigned";
    public const string AdminAssigned = "adminAssigned";
    public const string UserSelfSubscribed = "userSelfSubscribed";
}

public static class NotificationRecipientSources
{
    public const string DirectSubscription = "directSubscription";
    public const string OperationalTeamMembership = "operationalTeamMembership";
    public const string AttentionRoutingIntegrationOverride = "attentionRoutingIntegrationOverride";
}

public static class NotificationRecipientSkipReasons
{
    public const string UserOptOut = "userOptOut";
    public const string DeliveryModeMuted = "deliveryModeMuted";
    public const string DeliveryModeDigestOnly = "deliveryModeDigestOnly";
    public const string ContactDisabled = "contactDisabled";
    public const string MissingEmail = "missingEmail";
    public const string UserDisabled = "userDisabled";
    public const string TeamDisabled = "teamDisabled";
    public const string TeamEmpty = "teamEmpty";
    public const string DuplicateEmail = "duplicateEmail";
    public const string NoEmailSubscription = "noEmailSubscription";
    public const string EmailOptedOut = "emailOptedOut";
}
