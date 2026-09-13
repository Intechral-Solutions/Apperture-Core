namespace Apperture.Core.Domain.Identity;

/// <summary>
/// A curated built-in avatar illustration. Labels describe the illustration as a visual option so the
/// catalog never asserts anything about the person choosing it.
/// </summary>
public sealed record BuiltInAvatarOption(string Id, string DisplayName, string Description);

/// <summary>
/// The deliberately small, fixed set of platform-owned avatar illustrations. This is not an
/// extensible catalog: new entries are a product decision, not configuration.
/// </summary>
public static class BuiltInAvatarCatalog
{
    public const string OptionAId = "option-a";
    public const string OptionBId = "option-b";
    public const string OptionCId = "option-c";

    public const string DefaultOptionId = OptionAId;

    private static readonly BuiltInAvatarOption[] OptionList =
    [
        new(OptionAId, "Option A", "The first default illustration. This is the platform default."),
        new(OptionBId, "Option B", "The second default illustration."),
        new(OptionCId, "Option C", "The third default illustration."),
    ];

    public static IReadOnlyList<BuiltInAvatarOption> Options => OptionList;

    public static BuiltInAvatarOption DefaultOption => OptionList[0];

    /// <summary>
    /// Maps a stored or client-supplied id onto the current catalog, including retired AUP-1 ids.
    /// </summary>
    public static string? CanonicalizeOptionId(string? optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
            return null;

        var trimmed = optionId.Trim();
        if (OptionList.Any(o => string.Equals(o.Id, trimmed, StringComparison.Ordinal)))
            return trimmed;

        return trimmed switch
        {
            "neutral" or "neutral-bold" => OptionAId,
            "long-hair" => OptionBId,
            "short-hair" => OptionCId,
            _ => null
        };
    }

    public static bool IsKnownOptionId(string? optionId) => CanonicalizeOptionId(optionId) is not null;

    public static BuiltInAvatarOption? TryGet(string? optionId)
    {
        var canonical = CanonicalizeOptionId(optionId);
        return canonical is null
            ? null
            : OptionList.First(o => string.Equals(o.Id, canonical, StringComparison.Ordinal));
    }

    /// <summary>Validates a client-supplied option id and returns its canonical form.</summary>
    public static string RequireOptionId(string? optionId)
    {
        var option = TryGet(optionId)
            ?? throw new ArgumentException($"'{optionId}' is not a built-in avatar option.", nameof(optionId));
        return option.Id;
    }

    /// <summary>Client-relative path for the illustration asset. Drop replacement files here.</summary>
    public static string ImagePath(string? optionId)
    {
        var id = CanonicalizeOptionId(optionId) ?? DefaultOptionId;
        return $"images/account/avatars/{id}.jpg";
    }
}
