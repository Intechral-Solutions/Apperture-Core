namespace Apperture.Core.Domain.Notifications;

public static class NotificationDefinitionIds
{
    public const string PlatformScheduleExpectedRunMissing = "platform.schedule.expected_run_missing";
}

public static class NotificationSourceKinds
{
    public const string PlatformEvent = "platformEvent";
}

public static class NotificationDefinitionOrigins
{
    public const string PlatformBuiltIn = "platformBuiltIn";
    public const string ModuleBuiltIn = "moduleBuiltIn";
    public const string UserCreated = "userCreated";
}

public static class NotificationDefinitionScopes
{
    public const string Platform = "platform";
    public const string Module = "module";
    public const string Integration = "integration";
}

public static class NotificationDefinitionMutabilities
{
    public const string StaticRuleMutableSubscription = "staticRuleMutableSubscription";
    public const string FullyEditable = "fullyEditable";
    public const string ReadOnly = "readOnly";
}

public static class NotificationSchemaVersions
{
    public const string RuleV1 = "apperture.notification.rule/v1";
    public const string TemplateV1 = "apperture.notification.template/v1";
}
