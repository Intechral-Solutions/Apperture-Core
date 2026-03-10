using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces dependency rules for the runtime trust subsystem.
/// Core.Security.Trust must not depend on Core.Security.Secrets (enrollment is not generic secret storage).
/// </summary>
public sealed class RuntimeTrustDependencies
{
    [Fact]
    public void Core_Security_Trust_Should_Not_Depend_On_Core_Security_Secrets()
    {
        var trustAssembly = typeof(Apperture.Core.Security.Trust.RuntimeIdentity).Assembly;
        var result = Types.InAssembly(trustAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Core.Security.Secrets")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Core.Security.Trust must not depend on Core.Security.Secrets. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Core.Security.Trust must not reference Apperture.Core.Security.Secrets.");
    }
}

