using FluentAssertions;
using Proof.CampusNexus.StudentReview;
using Xunit;

namespace Apperture.UnitTests.Proof.CampusNexus;

public sealed class ProofStudentReviewODataQueryTests
{
    [Fact]
    public void Build_uses_defaults_and_escapes_segments()
    {
        var q = ProofStudentReviewODataQuery.Build();
        q.Should().Contain("$orderby=");
        q.Should().Contain("$top=10");
        q.Should().Contain("$select=");
        q.Should().Contain(Uri.EscapeDataString(ProofStudentReviewODataQuery.DefaultOrderBy));
    }

    [Fact]
    public void Build_respects_overrides()
    {
        var q = ProofStudentReviewODataQuery.Build("Id desc", 5, "Id");
        q.Should().Contain("$top=5");
        q.Should().Contain(Uri.EscapeDataString("Id desc"));
        q.Should().Contain(Uri.EscapeDataString("Id"));
    }
}
