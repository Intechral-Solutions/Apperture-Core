using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class NotificationEvaluateOnlyCoordinatorInstanceTests
{
    [Fact]
    public async Task Instances_disabled_does_not_create_rows()
    {
        await using var db = CreateDb();
        var builtin = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = builtin.Id.Value,
            DisplayName = builtin.DisplayName,
            SourceKind = builtin.SourceKind,
            Origin = builtin.Origin,
            Scope = builtin.Scope,
            Mutability = builtin.Mutability,
            PrimaryEventType = builtin.PrimaryEventType,
            IsEnabled = true,
            RuleJson = builtin.RuleJson,
            TemplateJson = builtin.TemplateJson,
            CreatedAtUtc = builtin.CreatedAtUtc,
            UpdatedAtUtc = builtin.UpdatedAtUtc,
        });
        await db.SaveChangesAsync();

        var evaluator = NotificationEvaluationTestHelpers.CreateEvaluator();

        var instanceCoordinator = new NotificationInstanceCoordinator(
            new SqlNotificationDefinitionStore(db),
            new NotificationRenderContextBuilder(),
            new NotificationInstanceCoordinatorTests.StubLiveRenderContextResolver(),
            new NotificationTemplateRenderer(),
            new SqlNotificationRecipientResolver(
                db,
                new SqlNotificationSubscriptionStore(db),
                new SqlNotificationDefinitionStore(db),
                new OperationalTeamRecipientExpander(db)),
            new SqlNotificationInstanceStore(db),
            new SqlNotificationDeliveryOutboxStore(db),
            new StubAttentionResolver(),
            new StubIncidentTracker(),
            new NoOpRedactionPolicy(),
            new NotificationInstanceCoordinatorTests.NoOpUserInboxService(),
            Options.Create(new NotificationInstanceOptions { Enabled = true }),
            NullLogger<NotificationInstanceCoordinator>.Instance);

        var sut = new NotificationEvaluateOnlyCoordinator(
            new SqlNotificationDefinitionStore(db),
            evaluator,
            instanceCoordinator,
            Options.Create(new NotificationEvaluationOptions { Enabled = true }),
            Options.Create(new NotificationInstanceOptions { Enabled = false }),
            NullLogger<NotificationEvaluateOnlyCoordinator>.Instance);

        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");

        await sut.EvaluatePlatformEventAsync(envelope);

        (await db.NotificationInstances.CountAsync()).Should().Be(0);
    }

    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubAttentionResolver : IAttentionRecipientResolver
    {
        public ValueTask<AttentionRecipientResolution> ResolveInstanceDefaultsAsync(
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<AttentionRecipientResolution> ResolveAsync(
            IntegrationId integrationId,
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubIncidentTracker : IAttentionIncidentTracker
    {
        public ValueTask<IReadOnlySet<string>> GetIntegrationIdsWithActiveConditionAsync(
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public ValueTask<AttentionIncidentObserveResult> ObserveScheduleExpectationAsync(
            IntegrationId integrationId,
            ScheduleExpectationSnapshot snapshot,
            DateTimeOffset observedAtUtc,
            string? resourceContextId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlyList<AttentionIncidentProjection>> ListRecentForIntegrationAsync(
            IntegrationId integrationId,
            string conditionKey,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AttentionIncidentProjection>>([]);

        public ValueTask<AttentionIncidentProjection?> GetIncidentAsync(
            IntegrationId integrationId,
            string incidentId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AttentionIncidentProjection?>(null);

        public ValueTask<bool> AcknowledgeAsync(
            IntegrationId integrationId,
            string incidentId,
            string acknowledgedBy,
            string? note,
            DateTimeOffset acknowledgedAtUtc,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
