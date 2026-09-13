using System.Linq;
using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Epic 73 / preflight: connector packages register into resolution; ConnectorResolution must stay free of direct
/// references to specific connector implementation assemblies so hosts compose connectors explicitly.
/// </summary>
public sealed class ConnectorResolutionDependencies
{
    [Fact]
    public void ConnectorResolution_Should_Not_Reference_Specific_Connector_Implementation_Assemblies()
    {
        var assembly = typeof(Apperture.ConnectorResolution.ConnectorClientFactoryRegistry).Assembly;
        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Apperture.Connectors.CampusNexus",
                "Apperture.Connectors.Sftp")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"ConnectorResolution must not reference connector implementation assemblies. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.ConnectorResolution must not take a compile-time dependency on Apperture.Connectors.*.");
    }
}
