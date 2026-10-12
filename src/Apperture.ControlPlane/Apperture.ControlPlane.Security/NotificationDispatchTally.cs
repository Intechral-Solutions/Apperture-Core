using Apperture.Core.Application.Notifications.Delivery;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// What the transport reported for each send a dispatch made, counted as the sends happen (AUD-1.4 slice 6). A send that was not
/// attempted (the channel is not configured) is counted separately and is not an attempt. Acceptance is not delivery.
/// </summary>
public sealed class NotificationDispatchTally
{
    public int Accepted { get; private set; }

    public int Rejected { get; private set; }

    public int Failed { get; private set; }

    /// <summary>Sends cancelled before an answer: the message may or may not have left.</summary>
    public int Unknown { get; private set; }

    public int NotAttempted { get; private set; }

    /// <summary>The sends that reached the transport and ended with an observation.</summary>
    public int Attempted => Accepted + Rejected + Failed + Unknown;

    internal void Record(EmailSendDisposition observed)
    {
        switch (observed)
        {
            case EmailSendDisposition.Accepted:
                Accepted++;
                break;
            case EmailSendDisposition.Rejected:
                Rejected++;
                break;
            case EmailSendDisposition.Cancelled:
                Unknown++;
                break;
            case EmailSendDisposition.NotAttempted:
                NotAttempted++;
                break;
            default:
                Failed++;
                break;
        }
    }
}
