using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NotificationInstancesControllerTests
{
    [Fact]
    public async Task List_ExposesResourceContextId_OnSummaryDto()
    {
        var instances = new Mock<INotificationInstanceQueryService>();
        var instance = NewInstance("app-a");
        instances.Setup(x => x.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { instance });
        var controller = new NotificationInstancesController(instances.Object);

        var result = await controller.List(null, null, null);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<NotificationInstanceSummaryDto>>().Subject;
        dtos.Should().ContainSingle(d => d.ResourceContextId == "app-a");
    }

    [Fact]
    public async Task List_passes_resourceContextId_into_query()
    {
        NotificationInstanceListQuery? captured = null;
        var instances = new Mock<INotificationInstanceQueryService>();
        instances.Setup(x => x.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationInstanceListQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(Array.Empty<NotificationInstance>());
        var controller = new NotificationInstancesController(instances.Object);

        var result = await controller.List(null, null, null, resourceContextId: "app-a");

        result.Result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task Get_ExposesResourceContextId_OnDetailDto()
    {
        var instances = new Mock<INotificationInstanceQueryService>();
        var instance = NewInstance("app-b");
        instances.Setup(x => x.GetAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var controller = new NotificationInstancesController(instances.Object);

        var result = await controller.Get("id-1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<NotificationInstanceDetailDto>().Subject;
        dto.ResourceContextId.Should().Be("app-b");
    }

    private static NotificationInstance NewInstance(string resourceContextId) =>
        new(
            Id: NotificationInstanceId.From("id-1"),
            NotificationDefinitionId: "def-1",
            NotificationKind: "kind-1",
            Status: "status-1",
            PlatformEventId: null,
            AttentionIncidentId: null,
            IntegrationId: null,
            ModuleId: null,
            RunId: null,
            ScheduleId: null,
            InstanceDedupeKey: "dedupe-1",
            RenderedSubject: "subject",
            RenderedSummary: "summary",
            RenderedBodyText: "body-text",
            RenderedBodyHtml: null,
            RenderedActionText: null,
            RenderedActionUrl: null,
            RenderedContentFieldsJson: null,
            TemplateVersionHash: "v1",
            RenderContextJson: "{}",
            RecipientSnapshotJson: null,
            SuppressionReason: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ResourceContextId: resourceContextId);
}
