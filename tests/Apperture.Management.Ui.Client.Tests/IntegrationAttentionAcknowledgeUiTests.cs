using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Integrations;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-2.1: the real schedule diagnostics page, rendered by bUnit. Acknowledging an attention incident needs
/// <c>integration.update</c>, so the action is offered on that permission and not on <c>integration.read</c>. An acknowledgement the
/// server refuses because the incident resolved first (409) or because the permission is missing (403) is reported next to the
/// incident history, which stays visible and is reloaded.
/// </summary>
public sealed class IntegrationAttentionAcknowledgeUiTests : BunitContext
{
    private const string IntegrationId = "int-1";
    private const string ActiveId = "attn_active";
    private const string ResolvedId = "attn_resolved";
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");

    private readonly Mock<IManagementClient> _client = new(MockBehavior.Loose);

    public IntegrationAttentionAcknowledgeUiTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    // ---- who is offered the action ----

    [Fact]
    public void An_operator_who_can_update_integrations_is_offered_Acknowledge_on_an_active_incident_only()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));

        AcknowledgeButtons(cut).Should().ContainSingle("only the active incident can be acknowledged");
        cut.FindAll("tbody tr").Should().HaveCount(2, "the resolved incident is listed but has no action");
    }

    [Fact]
    public void A_read_only_operator_sees_the_incident_history_and_no_Acknowledge_action()
    {
        var cut = RenderPage(Capabilities(canUpdate: false));

        AcknowledgeButtons(cut).Should().BeEmpty("integration.read is not enough to acknowledge");
        cut.Markup.Should().Contain("Expected-run-missing incident history");
        cut.FindAll("tbody tr").Should().HaveCount(2, "the history itself is still shown");
    }

    [Fact]
    public void A_failed_capability_lookup_hides_the_action_and_leaves_the_history_readable()
    {
        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("boom"));

        var cut = RenderPage(capabilities.Object);

        AcknowledgeButtons(cut).Should().BeEmpty("the UI fails closed");
        cut.FindAll("tbody tr").Should().HaveCount(2);
    }

    // ---- acknowledging ----

    [Fact]
    public void Acknowledging_sends_the_request_without_a_note_and_reloads_the_history()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));
        _client.Setup(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AcknowledgeButtons(cut).Single().Click();

        _client.Verify(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()), Times.Once);
        cut.WaitForAssertion(() => _client.Verify(
            c => c.GetIntegrationScheduleDiagnosticsAsync(IntegrationId, It.IsAny<CancellationToken>()), Times.Exactly(2)));
        cut.FindAll("#integration-attention-ack-notice").Should().BeEmpty();
    }

    [Fact]
    public void A_409_reports_that_the_incident_resolved_and_keeps_the_reloaded_history_visible()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));
        _client.Setup(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("The attention incident is resolved and can no longer be acknowledged.", null, HttpStatusCode.Conflict));

        AcknowledgeButtons(cut).Single().Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#integration-attention-ack-notice").TextContent.Should().Contain("already resolved");
            cut.FindAll("tbody tr").Should().HaveCount(2, "the history is reloaded and still on the page");
        });
        _client.Verify(c => c.GetIntegrationScheduleDiagnosticsAsync(IntegrationId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void A_403_says_the_permission_is_missing_and_keeps_the_history_visible()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));
        _client.Setup(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden", null, HttpStatusCode.Forbidden));

        AcknowledgeButtons(cut).Single().Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#integration-attention-ack-notice").TextContent.Should().Contain("do not have permission");
            cut.FindAll("tbody tr").Should().HaveCount(2);
        });
    }

    [Fact]
    public void An_unknown_incident_keeps_the_pages_existing_error_state()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));
        _client.Setup(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        AcknowledgeButtons(cut).Single().Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Incident not found or could not be acknowledged."));
    }

    [Fact]
    public void Any_other_failure_shows_the_servers_message_as_before()
    {
        var cut = RenderPage(Capabilities(canUpdate: true));
        _client.Setup(c => c.AcknowledgeIntegrationAttentionIncidentAsync(IntegrationId, ActiveId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("The note is limited to 1024 characters.", null, HttpStatusCode.BadRequest));

        AcknowledgeButtons(cut).Single().Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("The note is limited to 1024 characters."));
    }

    // ---- wiring ----

    private static IReadOnlyList<AngleSharp.Dom.IElement> AcknowledgeButtons(IRenderedComponent<IntegrationScheduleDiagnostics> cut) =>
        cut.FindAll("button").Where(b => b.TextContent.Trim() == "Acknowledge").ToList();

    private static IOperatorCapabilitiesService Capabilities(bool canUpdate)
    {
        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(false, false, false, false, false, CanViewIntegrations: true, CanUpdateIntegrations: canUpdate));
        return capabilities.Object;
    }

    private IRenderedComponent<IntegrationScheduleDiagnostics> RenderPage(IOperatorCapabilitiesService capabilities)
    {
        _client.Setup(c => c.ListOperationalTeamsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OperationalTeamSummaryDto>)[]);
        _client.Setup(c => c.GetIntegrationScheduleDiagnosticsAsync(IntegrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Diagnostics());

        var nav = new TestNavigationManager("https://localhost/", $"https://localhost/integrations/{IntegrationId}/schedule-diagnostics");
        var session = new SessionStateService(_client.Object);
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(capabilities);
        Services.AddSingleton<NavigationManager>(nav);
        Services.AddSingleton(new ShellOrientationService());
        Services.AddSingleton<IToastService>(new ToastService());
        Services.AddSingleton(session);
        Services.AddSingleton(new ResourceContextViewState(new FakeJsRuntime(), nav, _client.Object, session));

        return Render<IntegrationScheduleDiagnostics>(p => p.Add(x => x.IntegrationId, IntegrationId));
    }

    private static IntegrationScheduleDiagnosticsDto Diagnostics() =>
        new(
            IntegrationId,
            HasRecurringScheduleExpectation: true,
            EvaluationStatus: "MissingExpectedRun",
            InGrace: false,
            GraceReason: null,
            GraceAnchorUtc: null,
            MostRecentExpectedFireUtc: T0,
            LastSatisfyingScheduleOriginAuthorizedRunId: null,
            LastSatisfyingScheduleOriginAuthorizedRunAt: null,
            ReferenceScheduleId: "sch1",
            ReferenceScheduleEnabled: true,
            LastScheduleEnabledAtUtc: null,
            LastScheduleDisabledAtUtc: null,
            MaterialScheduleSemanticsChangedAtUtc: null,
            LastAuthorizedRunId: null,
            LastAuthorizedRunStatus: null,
            LastAuthorizedRunAt: null,
            RecentExpectedFires: [],
            RecentAttentionIncidents:
            [
                new AttentionIncidentDto(ActiveId, IntegrationId, "expected_run_missing", "Active", T0, T0, null, null, null, null, null, null, null),
                new AttentionIncidentDto(ResolvedId, IntegrationId, "expected_run_missing", "Resolved", T0.AddDays(-1), T0.AddDays(-1), T0.AddHours(-23), "ExpectationCleared", null, null, null, null, null),
            ],
            AttentionRouting: new AttentionRoutingEffectiveDto(
                HasRoute: false, Enabled: true, PrimaryRecipients: [], PrimarySource: "None", EscalationRecipients: [], EscalationSource: "None",
                PrimaryOperationalTeamId: null, PrimaryOperationalTeamName: null, EscalationOperationalTeamId: null, EscalationOperationalTeamName: null,
                HasPrimaryRoute: false, HasEscalationRoute: false, PrimaryNoRouteReason: null, EscalationNoRouteReason: null,
                ResolvedPrimaryPrincipalUserIds: [], ResolvedEscalationPrincipalUserIds: [], ResolvedPrimaryExternalRecipients: [],
                ResolvedEscalationExternalRecipients: [], EscalationAfterTicks: null, ReminderIntervalTicks: null, SendResolvedNotification: true,
                SuppressRemindersWhenAcknowledged: true, SuppressEscalationWhenAcknowledged: true,
                IntegrationRowPrimaryOperationalTeamId: null, IntegrationRowEscalationOperationalTeamId: null),
            RecentPlatformNotificationInstances: [],
            RecentPlatformNotificationOutbox: []);
}
