using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Api.Controllers;
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

    private sealed class StubNotificationDefinitionService : INotificationDefinitionService
    {
        private readonly IReadOnlyList<NotificationDefinition> _rows;
        public StubNotificationDefinitionService(IReadOnlyList<NotificationDefinition> rows) => _rows = rows;

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

        public ValueTask<NotificationValidationResult> ValidateAsync(
            NotificationDefinitionValidateRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NotificationValidationResult.Success());

        public ValueTask<RenderedNotificationSnapshot> RenderPreviewAsync(
            string? definitionId,
            NotificationDefinitionRenderPreviewRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
