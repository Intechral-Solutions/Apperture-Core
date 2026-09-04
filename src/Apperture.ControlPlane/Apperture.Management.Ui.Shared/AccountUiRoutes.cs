namespace Apperture.Management.Ui.Shared;

/// <summary>One section of the account area, as shown in the account section navigation.</summary>
public sealed record AccountSection(string Label, string Href, string Description);

/// <summary>
/// Routes for the personal account area. The account root and the legacy <c>/preferences</c> path
/// both redirect into this set so existing links and bookmarks keep working.
/// </summary>
public static class AccountUiRoutes
{
    public const string Root = "/account";
    public const string Profile = "/account/profile";
    public const string Notifications = "/account/notifications";
    public const string Preferences = "/account/preferences";
    public const string Security = "/account/security";

    public const string VerifyEmail = "/account/verify-email";
    public const string SetupPassword = "/account/setup-password";
    public const string ResetPassword = "/account/reset-password";
    public const string ForgotPassword = "/account/forgot-password";

    /// <summary>Pre-AUP-1 path, kept as a redirect to <see cref="Preferences"/>.</summary>
    public const string LegacyPreferences = "/preferences";

    /// <summary>The personal notification inbox. Separate surface: messages, not preferences.</summary>
    public const string NotificationInbox = "/me/notifications";

    public static IReadOnlyList<AccountSection> Sections { get; } =
    [
        new("Profile", Profile, "Your name, photo, and identity."),
        new("Notifications", Notifications, "Which notifications reach you, and how."),
        new("Appearance & regional", Preferences, "Theme, timezone, and clock format."),
        new("Security", Security, "Password and sign-in methods."),
    ];

    /// <summary>
    /// Matches a path to its account section so the section navigation can mark the current item.
    /// The account root resolves to Profile, matching the redirect.
    /// </summary>
    public static AccountSection? ResolveSection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = Normalize(path);
        if (string.Equals(normalized, Root, StringComparison.OrdinalIgnoreCase))
            return Sections[0];

        return Sections.FirstOrDefault(s => string.Equals(s.Href, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAccountPath(string? path) => ResolveSection(path) is not null;

    private static string Normalize(string path)
    {
        var value = path.Trim();
        var queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            value = value[..queryIndex];
        if (value.Length > 1)
            value = value.TrimEnd('/');
        return value.StartsWith('/') ? value : "/" + value;
    }
}
