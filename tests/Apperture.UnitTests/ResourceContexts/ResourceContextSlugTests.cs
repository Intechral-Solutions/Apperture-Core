using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Slug normalization is deterministic and only normalized slugs are ever persisted.
/// </summary>
public sealed class ResourceContextSlugTests
{
    [Theory]
    [InlineData("Payments", "payments")]
    [InlineData("  Payments  ", "payments")]
    [InlineData("Student Records", "student-records")]
    [InlineData("student_records", "student-records")]
    [InlineData("Student   Records", "student-records")]
    [InlineData("student---records", "student-records")]
    [InlineData("-student-records-", "student-records")]
    [InlineData("Student Récords!", "student-rcords")]
    [InlineData("Finance & Ops", "finance-ops")]
    [InlineData("A1", "a1")]
    [InlineData("ALL CAPS", "all-caps")]
    [InlineData("tabs\tand\nnewlines", "tabs-and-newlines")]
    public void Normalize_ProducesExpectedSlug(string candidate, string expected) =>
        ResourceContextSlug.Normalize(candidate).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("___")]
    [InlineData("!!!")]
    public void TryNormalize_RejectsCandidatesThatNormalizeToEmpty(string? candidate) =>
        ResourceContextSlug.TryNormalize(candidate, out _).Should().BeFalse();

    [Fact]
    public void Normalize_ThrowsWhenResultWouldBeEmpty()
    {
        var act = () => ResourceContextSlug.Normalize("***");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = ResourceContextSlug.Normalize("Student Records");
        ResourceContextSlug.Normalize(once).Should().Be(once);
    }

    [Fact]
    public void Normalize_TruncatesToMaxLengthWithoutTrailingHyphen()
    {
        var normalized = ResourceContextSlug.Normalize(new string('a', ResourceContextSlug.MaxLength + 40));
        normalized.Length.Should().Be(ResourceContextSlug.MaxLength);
        normalized.Should().NotEndWith("-");
    }

    /// <summary>
    /// CTX-1 reserves only what is actually taken or withheld. Speculative route terms such as
    /// <c>api</c>, <c>apps</c> and <c>new</c> are deliberately not reserved until a slug-addressed
    /// route needs them.
    /// </summary>
    [Fact]
    public void ReservedSlugs_AreExactlySharedAndSystem() =>
        ResourceContextSlug.ReservedSlugs.Should().BeEquivalentTo(["shared", "system"]);

    [Theory]
    [InlineData("shared", true)]
    [InlineData("system", true)]
    [InlineData("api", false)]
    [InlineData("apps", false)]
    [InlineData("new", false)]
    [InlineData("payments", false)]
    public void IsReserved_MatchesTheReservedSet(string slug, bool expected) =>
        ResourceContextSlug.IsReserved(slug).Should().Be(expected);
}
