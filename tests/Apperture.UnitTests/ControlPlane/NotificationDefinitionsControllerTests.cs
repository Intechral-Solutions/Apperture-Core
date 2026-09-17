using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NotificationDefinitionsControllerTests
{
    [Fact]
    public async Task List_returns_summaries()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var service = new StubNotificationDefinitionService([def]);
        var sut = new NotificationDefinitionsController(service);

        var action = await sut.List(
            integrationId: null,
            eventType: null,
            enabled: null,
            origin: null,
            cancellationToken: CancellationToken.None);
        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<Apperture.Management.Client.NotificationDefinitionSummaryDto>>().Subject;
        rows.Should().ContainSingle(x => x.Id == def.Id.Value);
    }

    [Fact]
    public async Task RenderPreviewDraft_uses_null_definition_id()
    {
        var snapshot = new RenderedNotificationSnapshot(
            "Subject",
            "Summary",
            "Body",
            null,
            null,
            null,
            null,
            "hash");
        var service = new StubNotificationDefinitionService([], snapshot);
        var sut = new NotificationDefinitionsController(service);

        var action = await sut.RenderPreviewDraft(
            new RenderNotificationPreviewRequest(
                """{"schemaVersion":"apperture.notification.template/v1","subject":"S","summary":"Su","bodyText":"B"}""",
                null,
                new NotificationRenderSampleDto(
                    PlatformEventTypes.ScheduleExpectedRunMissing,
                    DateTimeOffset.UtcNow,
                    "warning",
                    "int-1",
                    null,
                    "activation",
                    null,
                    null,
                    null,
                    null)),
            CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<RenderedNotificationSnapshotDto>();
        service.LastRenderDefinitionId.Should().BeNull();
    }

    private sealed class StubNotificationDefinitionService : INotificationDefinitionService
    {
        private readonly IReadOnlyList<NotificationDefinition> _rows;
        private readonly RenderedNotificationSnapshot? _previewSnapshot;

        public string? LastRenderDefinitionId { get; private set; }

        public StubNotificationDefinitionService(
            IReadOnlyList<NotificationDefinition> rows,
            RenderedNotificationSnapshot? previewSnapshot = null)
        {
            _rows = rows;
            _previewSnapshot = previewSnapshot;
        }

        public ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
            NotificationDefinitionListQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_rows);

        public ValueTask<NotificationDefinition?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_rows.FirstOrDefault(x => x.Id.Value == id));

        public ValueTask<NotificationDefinition> CreateAsync(
            NotificationDefinitionCreateRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<NotificationDefinition?> UpdateAsync(
            string id,
            NotificationDefinitionUpdateRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<NotificationDefinition?> SetEnabledAsync(
            string id,
            bool isEnabled,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<NotificationValidationResult> ValidateAsync(
            NotificationDefinitionValidateRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NotificationValidationResult.Success());

        public ValueTask<RenderedNotificationSnapshot> RenderPreviewAsync(
            string? definitionId,
            NotificationDefinitionRenderPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRenderDefinitionId = definitionId;
            return ValueTask.FromResult(
                _previewSnapshot
                ?? new RenderedNotificationSnapshot("S", "Su", "B", null, null, null, null, "hash"));
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
            IReadOnlyCollection<string> integrationIds,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
