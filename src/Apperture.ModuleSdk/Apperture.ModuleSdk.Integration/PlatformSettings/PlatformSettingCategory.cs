namespace Apperture.Core.Application.PlatformSettings;

public enum PlatformSettingCategory
{
    Notifications,
    PlatformEvents,
    AttentionIncidents,

    /// <summary>SWM-3: schedule timezone defaults and related scheduling controls.</summary>
    Scheduling,

    /// <summary>Development-gated demo/gallery settings (Epic 120). Not production controls.</summary>
    Demo,

    /// <summary>Integration-instance settings contributed by modules (SDK-6C). Not platform-wide controls such as SMTP.</summary>
    Integrations
}
