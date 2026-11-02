using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Apperture.IntegrationTests;

/// <summary>
/// An opt-in benchmark is not a regression test, and this assembly allows neither a Skipped result nor a test that reports Passed
/// without doing its work (scripts/dev/Test-SqlExecutionHonesty.ps1). So a benchmark that was not requested is not discovered at
/// all: it is absent from the report instead of Passed or Skipped. It is requested by setting its environment variable to exactly
/// <c>1</c> (see tests/README.md); any other value leaves it undiscovered.
/// </summary>
public static class OptInBenchmarkGate
{
    public const string AuditClassificationEnvironmentVariable = "APPERTURE_AUDIT_BENCHMARK";

    public static bool Requested(string environmentVariable) =>
        string.Equals(Environment.GetEnvironmentVariable(environmentVariable), "1", StringComparison.Ordinal);
}

/// <summary>
/// <see cref="FactAttribute"/> for a benchmark that exists only when requested through the named environment variable.
/// Once requested it is an ordinary fact: it runs its full workload and fails normally if the SQL it needs is unavailable.
/// </summary>
[XunitTestCaseDiscoverer("Apperture.IntegrationTests.OptInBenchmarkDiscoverer", "Apperture.IntegrationTests")]
public sealed class OptInBenchmarkFactAttribute : FactAttribute
{
    public OptInBenchmarkFactAttribute(string environmentVariable) => EnvironmentVariable = environmentVariable;

    public string EnvironmentVariable { get; }
}

/// <summary>Discovers the test like <see cref="FactAttribute"/> does, but only when the benchmark was requested.</summary>
public sealed class OptInBenchmarkDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly FactDiscoverer _fact;

    public OptInBenchmarkDiscoverer(IMessageSink diagnosticMessageSink) => _fact = new FactDiscoverer(diagnosticMessageSink);

    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        var environmentVariable = factAttribute.GetConstructorArguments().Single().ToString()!;
        return OptInBenchmarkGate.Requested(environmentVariable)
            ? _fact.Discover(discoveryOptions, testMethod, factAttribute)
            : [];
    }
}
