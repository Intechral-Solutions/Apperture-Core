using Apperture.Core.Application.PlatformEvents;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class PlatformEventsCatalogControllerTests
{
    [Fact]
    public async Task List_returns_catalog_entries()
    {
        var entry = new PlatformEventAuthoringCatalogEntry(
            PlatformEventTypes.ScheduleExpectedRunMissing,
            "/platform/readiness",
            "Expected run missing",
            "desc",
            ["warning"],
            RequiresIntegrationId: true,
            RequiresScheduleId: false,
            RequiresRunId: false,
            ["evaluationStatus"],
            ["event.type", "integration.id"]);
        var service = new StubCatalogService([entry]);
        var sut = new PlatformEventsCatalogController(service);

        var action = await sut.List(CancellationToken.None);
        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<PlatformEventCatalogEntryDto>>().Subject;
        rows.Should().ContainSingle(x => x.Type == PlatformEventTypes.ScheduleExpectedRunMissing);
        rows[0].TemplateVariables.Should().Contain("event.type");
    }

    [Fact]
    public async Task Get_returns_not_found_for_unknown_type()
    {
        var sut = new PlatformEventsCatalogController(new StubCatalogService([]));

        var action = await sut.Get("apperture.unknown", CancellationToken.None);
        action.Result.Should().BeOfType<NotFoundResult>();
    }

    private sealed class StubCatalogService : IPlatformEventAuthoringCatalogService
    {
        private readonly IReadOnlyList<PlatformEventAuthoringCatalogEntry> _rows;
        public StubCatalogService(IReadOnlyList<PlatformEventAuthoringCatalogEntry> rows) => _rows = rows;

        public ValueTask<IReadOnlyList<PlatformEventAuthoringCatalogEntry>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_rows);

        public ValueTask<PlatformEventAuthoringCatalogEntry?> GetAsync(
            string eventType,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_rows.FirstOrDefault(r => r.Type == eventType));
    }
}
