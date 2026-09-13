using System.Text.Json;
using FluentAssertions;
using Proof.CampusNexus.StudentReview;
using Xunit;

namespace Apperture.UnitTests.Proof.CampusNexus;

public sealed class CnsStudentReviewODataRowJsonTests
{
    [Fact]
    public void Deserializes_camelCase_odata_shape()
    {
        const string json = """
            {"Id":42,"studentNumber":"S1","firstName":"A","lastName":"B","createdDateTime":"2024-01-02T00:00:00Z","lastModifiedDateTime":"2024-06-01T12:30:00Z"}
            """;
        var row = JsonSerializer.Deserialize<CnsStudentReviewODataRow>(json);
        row.Should().NotBeNull();
        row!.Id.Should().Be(42);
        row.StudentNumber.Should().Be("S1");
        row.FirstName.Should().Be("A");
        row.LastName.Should().Be("B");
        row.CreatedDateTime.Should().NotBeNull();
        row.LastModifiedDateTime.Should().NotBeNull();
    }
}
