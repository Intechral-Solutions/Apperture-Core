namespace Apperture.Core.Domain.Identity;

/// <summary>
/// How a user's avatar is produced. Presentation only — it carries no identity or demographic meaning.
/// </summary>
public enum UserAvatarKind
{
    /// <summary>One of the curated platform-owned avatar illustrations.</summary>
    BuiltIn = 0,

    /// <summary>A normalized image derived from an operator-supplied upload.</summary>
    Custom = 1
}

/// <summary>
/// The avatar currently selected by a user. <see cref="ImageKey"/> is a platform-generated logical
/// storage key, never a filesystem path and never client-supplied.
/// </summary>
public sealed record UserAvatar(
    UserAvatarKind Kind,
    string? BuiltInOptionId,
    string? ImageKey)
{
    /// <summary>Neutral built-in avatar. Used whenever a user has made no selection.</summary>
    public static UserAvatar Default { get; } =
        new(UserAvatarKind.BuiltIn, BuiltInAvatarCatalog.DefaultOptionId, null);

    public static UserAvatar ForBuiltIn(string optionId) =>
        new(UserAvatarKind.BuiltIn, BuiltInAvatarCatalog.RequireOptionId(optionId), null);

    public static UserAvatar ForCustomImage(string imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey))
            throw new ArgumentException("Avatar image key is required.", nameof(imageKey));
        return new UserAvatar(UserAvatarKind.Custom, null, imageKey.Trim());
    }

    /// <summary>
    /// Rebuilds an avatar from persisted columns, falling back to the neutral default whenever the
    /// stored combination is incomplete or unrecognized.
    /// </summary>
    public static UserAvatar FromStorage(string? kind, string? builtInOptionId, string? imageKey)
    {
        if (string.Equals(kind, UserAvatarStorageKinds.Custom, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(imageKey))
        {
            return new UserAvatar(UserAvatarKind.Custom, null, imageKey.Trim());
        }

        if (string.Equals(kind, UserAvatarStorageKinds.BuiltIn, StringComparison.Ordinal)
            && BuiltInAvatarCatalog.CanonicalizeOptionId(builtInOptionId) is { } canonical)
        {
            return new UserAvatar(UserAvatarKind.BuiltIn, canonical, null);
        }

        return Default;
    }

    public string StorageKind => Kind == UserAvatarKind.Custom
        ? UserAvatarStorageKinds.Custom
        : UserAvatarStorageKinds.BuiltIn;

    /// <summary>
    /// Stable token that changes whenever the rendered avatar changes. Used for cache busting on the
    /// avatar serving route so responses can be treated as immutable.
    /// </summary>
    public string VersionToken => Kind == UserAvatarKind.Custom
        ? ImageKey ?? BuiltInAvatarCatalog.DefaultOptionId
        : BuiltInOptionId ?? BuiltInAvatarCatalog.DefaultOptionId;
}

/// <summary>Persisted discriminator values for <see cref="UserAvatarKind"/>.</summary>
public static class UserAvatarStorageKinds
{
    public const string BuiltIn = "builtIn";
    public const string Custom = "custom";
}
