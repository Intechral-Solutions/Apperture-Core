namespace Apperture.Core.Application.Services;

/// <summary>Values persisted on <see cref="Persistence.SqlServer.ManagedContactEntity.Source"/>.</summary>
public static class ManagedContactSources
{
    public const string Custom = "Custom";
    public const string Entra = "Entra";
}

public static class ManagedContactIdentityProviders
{
    public const string Entra = "Entra";
}

/// <summary>Values persisted on identity link rows.</summary>
public static class ManagedContactIdentityLinkStatuses
{
    public const string Active = "Active";
    public const string Broken = "Broken";
    public const string Revoked = "Revoked";
}

/// <summary>DTO / UX labels for linked platform user state.</summary>
public static class UserLinkStatusValues
{
    public const string None = "None";
    public const string Linked = "Linked";
}

/// <summary>Whether notifications can be addressed to this contact.</summary>
public static class NotificationUsabilityStatusValues
{
    public const string Deliverable = "Deliverable";
    public const string MissingEmail = "MissingEmail";
    public const string ContactDisabled = "ContactDisabled";
}
