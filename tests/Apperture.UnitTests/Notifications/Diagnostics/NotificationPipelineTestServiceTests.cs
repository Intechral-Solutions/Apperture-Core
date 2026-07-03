using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class NotificationPipelineTestServiceTests
{
    [Fact]
    public async Task Smtp_test_succeeds_when_channel_sends()
    {
        var channel = new Mock<IEmailNotificationChannel>();
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(true, "msg-1", null));

        var sut = CreateSut(emailChannel: channel.Object);
        var result = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        result.Succeeded.Should().BeTrue();
        channel.Verify(
            c => c.SendAsync(
                It.Is<EmailNotificationSendRequest>(r => r.Recipient == "ops@example.com" && r.HtmlBody != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Smtp_test_surfaces_channel_failure()
    {
        var channel = new Mock<IEmailNotificationChannel>();
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(false, null, "smtp_not_configured"));

        var sut = CreateSut(emailChannel: channel.Object);
        var result = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        result.Succeeded.Should().BeFalse();
        result.FailureSummary.Should().Be("smtp_not_configured");
    }

    [Fact]
    public async Task Platform_pipeline_dry_run_evaluates_without_emitting_event()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var definition = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var evaluator = new Mock<INotificationRuleEvaluator>();
        evaluator.Setup(e => e.EvaluateDefinitionAsync(
                definition,
                It.IsAny<NotificationRuleEvaluationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRuleEvaluationResult(
                definition.Id.Value,
                NotificationMatchStatus.Matched,
                NotificationKinds.Activation,
                NotificationKinds.Activation,
                null,
                []));

        var recipients = new Mock<INotificationRecipientResolver>();
        recipients.Setup(r => r.ResolvePreviewAsync(
                definition.Id.Value,
                "int_test",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRecipientResolutionPreview(
                [new ResolvedNotificationRecipient(
                    "ops@example.com",
                    "Ops",
                    "user_1",
                    null,
                    NotificationUsabilityStatusValues.Deliverable,
                    NotificationRecipientSources.DirectSubscription,
                    "sub_1",
                    NotificationRecipientKinds.Principal)],
                []));

        var sut = CreateSut(
            definitions: definitions.Object,
            evaluator: evaluator.Object,
            recipients: recipients.Object,
            emitter: emitter.Object);

        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: true));

        result.DryRun.Should().BeTrue();
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.RuleEvaluation && s.Status == NotificationPipelineTestStageStatuses.Passed);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.PlatformEventEmit && s.Status == NotificationPipelineTestStageStatuses.Skipped);
        result.DeliverableRecipients.Should().HaveCount(1);
        emitter.Verify(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Platform_pipeline_live_emits_event_and_reports_blocked_recipients()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var eventId = PlatformEventId.New();
        emitter.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformEventEnvelope(
                eventId,
                PlatformEventTypes.ScheduleExpectedRunMissing,
                PlatformEventSources.PlatformReadiness,
                "1.0",
                "application/json",
                "{}",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Warning,
                null,
                null,
                "int_test",
                null,
                null,
                null,
                null,
                null,
                null,
                null));

        var definition = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var evaluator = new Mock<INotificationRuleEvaluator>();
        evaluator.Setup(e => e.EvaluateDefinitionAsync(
                definition,
                It.IsAny<NotificationRuleEvaluationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRuleEvaluationResult(
                definition.Id.Value,
                NotificationMatchStatus.Matched,
                NotificationKinds.Activation,
                NotificationKinds.Activation,
                null,
                []));

        var recipients = new Mock<INotificationRecipientResolver>();
        recipients.Setup(r => r.ResolvePreviewAsync(
                definition.Id.Value,
                "int_test",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRecipientResolutionPreview([], []));

        var instances = new Mock<INotificationInstanceStore>();
        instances.Setup(i => i.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(
            definitions: definitions.Object,
            evaluator: evaluator.Object,
            recipients: recipients.Object,
            emitter: emitter.Object,
            instances: instances.Object);

        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: false));

        result.PlatformEventId.Should().Be(eventId.Value);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.RecipientResolution
            && s.Status == NotificationPipelineTestStageStatuses.Blocked);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.InstanceCreation
            && s.Status == NotificationPipelineTestStageStatuses.Failed);
    }

    [Fact]
    public async Task Platform_pipeline_fails_configuration_when_instances_disabled()
    {
        var sut = CreateSut(instancesEnabled: false);
        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: true));

        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.Configuration
            && s.Status == NotificationPipelineTestStageStatuses.Failed);
    }

    private static NotificationPipelineTestService CreateSut(
        IEmailNotificationChannel? emailChannel = null,
        INotificationDefinitionStore? definitions = null,
        INotificationRuleEvaluator? evaluator = null,
        INotificationRecipientResolver? recipients = null,
        IPlatformEventEmitter? emitter = null,
        INotificationInstanceStore? instances = null,
        bool instancesEnabled = true)
    {
        var auth = new NotificationDefinitionAuthorization(
            new FixedPrincipalContext(new StubActor("user:admin", "Admin")),
            new AllowAuth(),
            new AllowAuthEval(),
            new StubIntegrationRepo());

        var dispatch = new NotificationDispatchProcessor(
            Mock.Of<INotificationDeliveryOutboxStore>(),
            instances ?? Mock.Of<INotificationInstanceStore>(),
            emailChannel ?? Mock.Of<IEmailNotificationChannel>(),
            Options.Create(new NotificationDeliveryOptions { Enabled = true }),
            TimeProvider.System,
            NullLogger<NotificationDispatchProcessor>.Instance);

        return new NotificationPipelineTestService(
            auth,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            Options.Create(new NotificationEvaluationOptions { Enabled = true }),
            Options.Create(new NotificationInstanceOptions { Enabled = instancesEnabled }),
            Options.Create(new NotificationDeliveryOptions { Enabled = true }),
            Options.Create(new EmailNotificationOptions
            {
                Enabled = true,
                SmtpHost = "smtp.example.com",
                FromAddress = "noreply@example.com",
            }),
            definitions ?? Mock.Of<INotificationDefinitionStore>(),
            evaluator ?? Mock.Of<INotificationRuleEvaluator>(),
            recipients ?? Mock.Of<INotificationRecipientResolver>(),
            emitter ?? Mock.Of<IPlatformEventEmitter>(),
            instances ?? Mock.Of<INotificationInstanceStore>(),
            dispatch,
            emailChannel ?? Mock.Of<IEmailNotificationChannel>(),
            TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ShellUi:ManagementHostUrl"] = "http://localhost:9001",
            }).Build(),
            NullLogger<NotificationPipelineTestService>.Instance);
    }

    private sealed class AllowAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? governedResource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AllowAuthEval : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubIntegrationRepo : IIntegrationRepository
    {
        public ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainIntegration?>(
                new DomainIntegration(
                    id,
                    "Test",
                    "mod-1",
                    null,
                    IntegrationState.Active,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));

        public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DomainIntegration>>([]);

        public ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        private readonly IActor _actor;
        public FixedPrincipalContext(IActor actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(_actor);
    }

    private sealed class StubActor : IActor
    {
        public StubActor(string id, string? name)
        {
            Id = new ActorId(id);
            DisplayName = name;
        }

        public ActorId Id { get; }
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName { get; }
    }
}
