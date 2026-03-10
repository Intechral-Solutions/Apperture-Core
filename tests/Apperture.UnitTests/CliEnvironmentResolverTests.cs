using Apperture.Cli.Bootstrap;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests;

public sealed class CliEnvironmentResolverTests
{
    [Fact]
    public void GetEnvironmentName_WhenNoEnvVarSet_ReturnsProduction()
    {
        // Clear both vars so we see default (test may run with env set; we assert the contract)
        var dotnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable);
        var aspnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, null);

            var result = CliEnvironmentResolver.GetEnvironmentName();

            result.Should().Be("Production");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, dotnet);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, aspnet);
        }
    }

    [Fact]
    public void GetEnvironmentName_WhenDOTNET_ENVIRONMENTSet_ReturnsThatValue()
    {
        var dotnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable);
        var aspnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, "Staging");
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, null);

            var result = CliEnvironmentResolver.GetEnvironmentName();

            result.Should().Be("Staging");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, dotnet);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, aspnet);
        }
    }

    [Fact]
    public void GetEnvironmentName_WhenBothSet_DOTNET_ENVIRONMENTTakesPrecedence()
    {
        var dotnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable);
        var aspnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, "Production");
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, "Development");

            var result = CliEnvironmentResolver.GetEnvironmentName();

            result.Should().Be("Production");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, dotnet);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, aspnet);
        }
    }

    [Fact]
    public void GetEnvironmentName_WhenDOTNETUnsetAndASPNETSet_ReturnsASPNETValue()
    {
        var dotnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable);
        var aspnet = Environment.GetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, "Development");

            var result = CliEnvironmentResolver.GetEnvironmentName();

            result.Should().Be("Development");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.DotnetEnvironmentVariable, dotnet);
            Environment.SetEnvironmentVariable(CliEnvironmentResolver.AspNetCoreEnvironmentVariable, aspnet);
        }
    }
}
