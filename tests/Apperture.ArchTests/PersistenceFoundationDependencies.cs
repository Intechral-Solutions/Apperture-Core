using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces dependency rules for the persistence foundation.
/// See docs/01-architecture/persistence-foundation.md and bootstrap-and-secrets-boundary.md.
/// Persistence must not reference Bootstrap (connection string resolution belongs to Bootstrap)
/// or RuntimePlane.Abstractions (workflow engine abstractions); it only receives a connection string and uses Core.Security.Secrets.
/// </summary>
public sealed class PersistenceFoundationDependencies
{
    [Fact]
    public void Persistence_SqlServer_Should_Not_Depend_On_Bootstrap()
    {
        var persistenceAssembly = typeof(Apperture.Persistence.SqlServer.AppertureDbContext).Assembly;
        var result = Types.InAssembly(persistenceAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Bootstrap")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Persistence must not depend on Bootstrap. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Persistence.SqlServer must not reference Apperture.Bootstrap.");
    }

    [Fact]
    public void Persistence_SqlServer_Should_Not_Depend_On_RuntimePlane_Abstractions()
    {
        var persistenceAssembly = typeof(Apperture.Persistence.SqlServer.AppertureDbContext).Assembly;
        var result = Types.InAssembly(persistenceAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.RuntimePlane.Abstractions")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Persistence must not depend on RuntimePlane.Abstractions. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Persistence.SqlServer must not reference Apperture.RuntimePlane.Abstractions.");
    }
}
