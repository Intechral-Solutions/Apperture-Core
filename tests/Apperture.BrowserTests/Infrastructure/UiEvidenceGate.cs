using Xunit;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Real-surface UI evidence (accessibility tree, keyboard order, themes, screenshots) is a second opt-in on top of
/// the browser suite: it needs <c>APPERTURE_BROWSER_TESTS=1</c> and <c>APPERTURE_UI_EVIDENCE=1</c>. The blocking
/// <c>browser-security</c> CI job sets only the first, so these never run there.
/// </summary>
public static class UiEvidenceGate
{
    public const string EnvironmentVariable = "APPERTURE_UI_EVIDENCE";

    public const string SkipReason =
        "Opt-in UI evidence. Set APPERTURE_BROWSER_TESTS=1 and APPERTURE_UI_EVIDENCE=1 to run (see tests/Apperture.BrowserTests/README.md).";

    public static bool Enabled =>
        BrowserTestGate.Enabled
        && string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);
}

/// <summary><see cref="FactAttribute"/> that is skipped unless the browser suite and UI evidence are both enabled.</summary>
public sealed class UiEvidenceFactAttribute : FactAttribute
{
    public UiEvidenceFactAttribute()
    {
        if (!UiEvidenceGate.Enabled)
            Skip = UiEvidenceGate.SkipReason;
    }
}
