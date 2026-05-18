using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class NotificationTemplateDocumentBuilderTests
{
    [Fact]
    public void Build_and_parse_round_trip()
    {
        var json = NotificationTemplateDocumentBuilder.Build(
            "Subject {{integration.id}}",
            "Summary",
            "Body",
            "Open",
            "/schedules",
            [new NotificationTemplateContentFieldRow { Label = "Status", Value = "{{event.data.evaluationStatus}}" }]);

        NotificationTemplateDocumentBuilder.TryParse(json, out var state).Should().BeTrue();
        state.Subject.Should().Contain("integration.id");
        state.ActionUrl.Should().Be("/schedules");
        state.ContentFields.Should().ContainSingle();
    }
}
