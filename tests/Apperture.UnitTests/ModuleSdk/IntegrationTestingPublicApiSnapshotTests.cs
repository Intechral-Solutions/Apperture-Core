using Apperture.ModuleSdk.Integration.Testing;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class IntegrationTestingPublicApiSnapshotTests
{
    private static readonly string[] ExpectedPublicTypes =
    [
        nameof(IntegrationProcedureTestHost),
        nameof(IntegrationProcedureExecution),
        nameof(IntegrationExecutionIdentity),
        nameof(IntegrationExecutionOrigin),
        nameof(TestTimeProvider),
        nameof(CapturedPlatformEvent),
        nameof(CapturedOperationalLog),
    ];

    [Fact]
    public void Integration_testing_assembly_exports_expected_public_types()
    {
        var assembly = typeof(IntegrationProcedureTestHost).Assembly;
        var exported = assembly.GetExportedTypes()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedPublicTypes.Where(x => !exported.Contains(x)).ToList();
        Assert.True(missing.Count == 0, $"Missing exported integration testing types: {string.Join(", ", missing)}");

        var unexpected = exported.Where(x => !ExpectedPublicTypes.Contains(x)).OrderBy(x => x).ToList();
        Assert.True(
            unexpected.Count == 0,
            $"Unexpected exported integration testing types (update snapshot intentionally): {string.Join(", ", unexpected)}");
    }
}
