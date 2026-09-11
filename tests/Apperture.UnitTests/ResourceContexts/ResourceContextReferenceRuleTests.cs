using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// A resource may reference resources in its own ResourceContext or in Shared, and nothing else.
/// </summary>
public sealed class ResourceContextReferenceRuleTests
{
    private static readonly ResourceContextId ApplicationA = new("app-a");
    private static readonly ResourceContextId ApplicationB = new("app-b");

    [Fact]
    public void SameApplicationContext_IsAllowed() =>
        ResourceContextReferenceRule.CanReference(ApplicationA, ApplicationA).Should().BeTrue();

    [Fact]
    public void ApplicationReferencingShared_IsAllowed() =>
        ResourceContextReferenceRule.CanReference(ApplicationA, ResourceContextIds.Shared).Should().BeTrue();

    [Fact]
    public void ApplicationReferencingAnotherApplication_IsRejected() =>
        ResourceContextReferenceRule.CanReference(ApplicationA, ApplicationB).Should().BeFalse();

    [Fact]
    public void SharedReferencingShared_IsAllowed() =>
        ResourceContextReferenceRule.CanReference(ResourceContextIds.Shared, ResourceContextIds.Shared).Should().BeTrue();

    /// <summary>Shared is a floor, not a ceiling: it cannot reach into an Application.</summary>
    [Fact]
    public void SharedReferencingAnApplication_IsRejected() =>
        ResourceContextReferenceRule.CanReference(ResourceContextIds.Shared, ApplicationA).Should().BeFalse();
}
