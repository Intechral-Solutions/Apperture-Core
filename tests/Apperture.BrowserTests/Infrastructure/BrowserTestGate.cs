using Xunit;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// The browser suite is opt-in: every test is skipped unless <c>APPERTURE_BROWSER_TESTS=1</c>, so a plain
/// <c>dotnet test Apperture.slnx</c> never starts hosts, SQL or browsers.
/// </summary>
public static class BrowserTestGate
{
    public const string EnvironmentVariable = "APPERTURE_BROWSER_TESTS";

    public const string SkipReason =
        "Opt-in browser suite. Set APPERTURE_BROWSER_TESTS=1 to run (see tests/Apperture.BrowserTests/README.md).";

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);
}

/// <summary><see cref="FactAttribute"/> that is skipped unless the browser suite is enabled.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (!BrowserTestGate.Enabled)
            Skip = BrowserTestGate.SkipReason;
    }
}

/// <summary><see cref="TheoryAttribute"/> that is skipped unless the browser suite is enabled.</summary>
public sealed class BrowserTheoryAttribute : TheoryAttribute
{
    public BrowserTheoryAttribute()
    {
        if (!BrowserTestGate.Enabled)
            Skip = BrowserTestGate.SkipReason;
    }
}
