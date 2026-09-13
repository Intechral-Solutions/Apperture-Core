namespace Apperture.Management.Ui.Shared;

public static class NotificationDeliveryModeOptions
{
    public static readonly IReadOnlyList<(string Value, string Label)> All =
    [
        ("immediate", "Immediate"),
        ("muted", "Muted"),
        ("digestOnly", "Digest only"),
        ("immediateAndDigest", "Immediate and digest"),
    ];
}
