using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Inbox;

public sealed class NotificationUserInboxRecipientExtractorTests
{
    [Fact]
    public void Uses_InApp_when_present()
    {
        var json = """
            {
              "deliverable": [
                { "email": "a@x.com", "userId": "user-deliverable", "recipientKind": "principal", "source": "directSubscription" }
              ],
              "skipped": [],
              "inApp": [
                { "email": null, "userId": "user-inapp", "recipientKind": "principal", "source": "directSubscription" }
              ]
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);

        recipients.Should().ContainSingle();
        recipients[0].UserId.Should().Be("user-inapp");
    }

    [Fact]
    public void Falls_back_to_Deliverable_when_InApp_absent()
    {
        var json = """
            {
              "deliverable": [
                { "email": "a@x.com", "userId": "user-a", "recipientKind": "principal", "source": "directSubscription" }
              ],
              "skipped": []
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);

        recipients.Should().ContainSingle();
        recipients[0].UserId.Should().Be("user-a");
    }

    [Fact]
    public void Falls_back_to_Deliverable_when_InApp_empty()
    {
        var json = """
            {
              "deliverable": [
                { "email": "a@x.com", "userId": "user-a", "recipientKind": "principal", "source": "directSubscription" }
              ],
              "skipped": [],
              "inApp": []
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);

        recipients.Should().ContainSingle();
        recipients[0].UserId.Should().Be("user-a");
    }
}
