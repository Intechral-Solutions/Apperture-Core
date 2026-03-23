using Apperture.Connectors.CampusNexus;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Connectors;

public sealed class CampusNexusApiClientColumnInFilterTests
{
    [Fact]
    public void ColumnInFilter_numeric_values_unquoted()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        using var client = new CampusNexusApiClient(http);
        client.ColumnInFilter("StudentId", new[] { "1", "2" }).Should().Be("StudentId eq 1 or StudentId eq 2");
    }

    [Fact]
    public void ColumnInFilter_string_values_quoted()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        using var client = new CampusNexusApiClient(http);
        client.ColumnInFilter("Name", new[] { "O'Brien" }).Should().Be("Name eq 'O''Brien'");
    }

    [Fact]
    public void ColumnInFilter_comma_split()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        using var client = new CampusNexusApiClient(http);
        client.ColumnInFilter("Id", "10, 20").Should().Be("Id eq 10 or Id eq 20");
    }
}
