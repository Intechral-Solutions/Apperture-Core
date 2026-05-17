using Apperture.Core.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationTemplateRendererTests
{
    [Fact]
    public void Render_activation_variant_matches_golden_snapshot()
    {
        if (!NotificationDocumentJson.TryDeserializeTemplate(
                ExpectedRunMissingNotificationDefinitionSeed.TemplateJson,
                out var template,
                out _))
        {
            throw new InvalidOperationException("Seed template invalid.");
        }

        var factory = new NotificationRenderContextFactory();
        var context = factory.FromSample(
            new NotificationRenderSample(
                "apperture.schedule.expected_run.missing",
                DateTimeOffset.Parse("2026-05-15T12:00:00Z"),
                "warning",
                "int-demo",
                "Demo Integration",
                "activation",
                "inc_abc",
                DateTimeOffset.Parse("2026-05-15T11:00:00Z"),
                "https://app.example",
                new Dictionary<string, string>
                {
                    ["evaluationStatus"] = "MissingExpectedRun",
                    ["operatorSummaryLine"] = "No run in window",
                    ["mostRecentExpectedFireUtc"] = "2026-05-15T10:00:00Z",
                }));

        var sut = new NotificationTemplateRenderer();
        var snapshot = sut.Render(template!, context, "activation");

        snapshot.Subject.Should().Be("[Apperture] Attention: expected_run_missing (int-demo)");
        snapshot.BodyText.Should().Contain("MissingExpectedRun");
        snapshot.BodyText.Should().Contain("No run in window");
        snapshot.TemplateVersionHash.Should().NotBeNullOrWhiteSpace();
    }
}
