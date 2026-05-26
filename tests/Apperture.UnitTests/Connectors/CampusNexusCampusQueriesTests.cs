using System.Text.Json;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Connectors;

public sealed class CampusNexusCampusQueriesTests
{
    [Fact]
    public void ActiveCampusesListODataQuery_includes_active_filter_order_and_select()
    {
        var query = CampusNexusCampusQueries.ActiveCampusesListODataQuery;

        query.Should().Contain("$filter=");
        query.Should().Contain("IsActive");
        query.Should().Contain("$orderby=");
        query.Should().Contain("Name");
        query.Should().Contain("$select=");
        query.Should().Contain("Id");
        query.Should().Contain("Name");
        query.Should().Contain("Code");
        query.Should().NotContain("$top=");
    }

    [Fact]
    public void Deserializes_campus_odata_row()
    {
        const string json = """{"Id":10,"Name":"Main Campus","Code":"MAIN"}""";
        var row = JsonSerializer.Deserialize<CampusODataRow>(json);

        row.Should().NotBeNull();
        row!.Id.Should().Be(10);
        row.Name.Should().Be("Main Campus");
        row.Code.Should().Be("MAIN");
    }

    [Fact]
    public async Task ListActiveCampusesAsync_queries_campuses_entity_set()
    {
        var expected = new[]
        {
            new CampusODataRow { Id = 1, Name = "Alpha", Code = "A" }
        };

        var client = new Mock<ICampusNexusApiClient>();
        client.Setup(c => c.QueryAsync<CampusODataRow>(
                It.Is<ApiQuery>(q =>
                    q.EntityCollectionName == CampusNexusCampusQueries.CampusEntitySet
                    && q.ODataQuery == CampusNexusCampusQueries.ActiveCampusesListODataQuery),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var rows = await CampusNexusCampusQueries.ListActiveCampusesAsync(client.Object, CancellationToken.None);

        rows.Should().BeEquivalentTo(expected);
    }
}
