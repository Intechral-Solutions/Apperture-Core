using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Notifications;
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
    public void ManagedContact_entry_in_InApp_resolves_kind_and_ref_from_ManagedContactId()
    {
        var json = """
            {
              "deliverable": [
                { "email": "linked-vendor@test.com", "userId": "user-linked", "managedContactId": "mc-linked", "recipientKind": "managedContact", "source": "directSubscription" }
              ],
              "skipped": [],
              "inApp": [
                { "email": "linked-vendor@test.com", "userId": "user-linked", "managedContactId": "mc-linked", "recipientKind": "managedContact", "source": "directSubscription" }
              ]
            }
            """;

        var recipients = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(json);

        recipients.Should().ContainSingle();
        recipients[0].UserId.Should().Be("user-linked");
        recipients[0].SourceRecipientKind.Should().Be(NotificationRecipientKinds.ManagedContact);
        recipients[0].SourceRecipientRef.Should().Be("mc-linked");
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
