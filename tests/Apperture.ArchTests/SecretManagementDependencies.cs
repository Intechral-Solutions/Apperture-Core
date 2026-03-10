using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces dependency rules for the operational secret management subsystem.
/// See docs/01-architecture/secrets-and-key-management.md and bootstrap-and-secrets-boundary.md.
/// Core.Security.Secrets (abstractions) must not depend on Bootstrap or Persistence.
/// </summary>
public sealed class SecretManagementDependencies
{
    [Fact]
    public void Core_Security_Secrets_Should_Not_Depend_On_Bootstrap()
    {
        var secretsAssembly = typeof(Apperture.Core.Security.Secrets.Abstractions.ISecretStore).Assembly;
        var result = Types.InAssembly(secretsAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Bootstrap")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Core.Security.Secrets must not depend on Bootstrap. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Core.Security.Secrets must not reference Apperture.Bootstrap.");
    }

    [Fact]
    public void Core_Security_Secrets_Should_Not_Depend_On_Persistence()
    {
        var secretsAssembly = typeof(Apperture.Core.Security.Secrets.Abstractions.ISecretStore).Assembly;
        var result = Types.InAssembly(secretsAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Persistence")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Core.Security.Secrets must not depend on Persistence. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Core.Security.Secrets must not reference Apperture.Persistence.");
    }
}
