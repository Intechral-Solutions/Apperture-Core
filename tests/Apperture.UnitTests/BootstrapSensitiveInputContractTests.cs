using Apperture.Cli.Bootstrap;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests;

public sealed class BootstrapSensitiveInputContractTests
{
    [Fact]
    public void ContractSummary_DoesNotNormalizePasswordViaArgv()
    {
        BootstrapSensitiveInputContract.ContractSummary
            .Should().Contain("stdin")
            .And.Contain("non-argv")
            .And.NotContain("--password");
    }
}
