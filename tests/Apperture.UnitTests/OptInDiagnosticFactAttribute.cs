using Xunit;

namespace Apperture.UnitTests;

/// <summary>
/// A measurement that asserts nothing about correctness is not regression coverage, so it must not report Passed in an ordinary run.
/// It is reported Skipped, with the reason, unless the named environment variable is exactly <c>1</c> (see tests/README.md).
/// </summary>
public static class OptInDiagnosticGate
{
    public static bool Enabled(string environmentVariable) =>
        string.Equals(Environment.GetEnvironmentVariable(environmentVariable), "1", StringComparison.Ordinal);

    public static string SkipReason(string environmentVariable) =>
        $"Opt-in diagnostic: it measures and asserts nothing, so it is not run as a regression test. Set {environmentVariable}=1 to run it (see tests/README.md).";
}

/// <summary><see cref="FactAttribute"/> that is skipped unless the named environment variable is exactly <c>1</c>.</summary>
public sealed class OptInDiagnosticFactAttribute : FactAttribute
{
    public OptInDiagnosticFactAttribute(string environmentVariable)
    {
        if (!OptInDiagnosticGate.Enabled(environmentVariable))
            Skip = OptInDiagnosticGate.SkipReason(environmentVariable);
    }
}
