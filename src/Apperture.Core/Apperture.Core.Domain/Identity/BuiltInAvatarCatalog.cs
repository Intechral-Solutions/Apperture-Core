namespace Apperture.Core.Domain.Identity;

/// <summary>
/// A curated built-in avatar illustration. Labels describe the illustration's visual style so the
/// catalog never asserts anything about the person choosing it.
/// </summary>
public sealed record BuiltInAvatarOption(string Id, string DisplayName, string Description);

/// <summary>
/// The deliberately small, fixed set of platform-owned avatar illustrations. This is not an
/// extensible catalog: new entries are a product decision, not configuration.
/// </summary>
public static class BuiltInAvatarCatalog
{
    public const string DefaultOptionId = "neutral";

    private static readonly BuiltInAvatarOption[] OptionList =
    [
        new(DefaultOptionId, "Neutral", "A plain silhouette. This is the platform default."),
        new("neutral-bold", "Neutral (bold)", "The plain silhouette drawn with heavier contrast."),
        new("short-hair", "Short hair", "A silhouette with a short hairstyle."),
        new("long-hair", "Long hair", "A silhouette with a longer hairstyle."),
    ];

    public static IReadOnlyList<BuiltInAvatarOption> Options => OptionList;

    public static BuiltInAvatarOption DefaultOption => OptionList[0];

    public static bool IsKnownOptionId(string? optionId) =>
        !string.IsNullOrWhiteSpace(optionId)
        && OptionList.Any(o => string.Equals(o.Id, optionId.Trim(), StringComparison.Ordinal));

    public static BuiltInAvatarOption? TryGet(string? optionId) =>
        string.IsNullOrWhiteSpace(optionId)
            ? null
            : OptionList.FirstOrDefault(o => string.Equals(o.Id, optionId.Trim(), StringComparison.Ordinal));

    /// <summary>Validates a client-supplied option id and returns its canonical form.</summary>
    public static string RequireOptionId(string? optionId)
    {
        var option = TryGet(optionId)
            ?? throw new ArgumentException($"'{optionId}' is not a built-in avatar option.", nameof(optionId));
        return option.Id;
    }
}
