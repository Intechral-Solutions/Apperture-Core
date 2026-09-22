namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Background poller that turns the incremental <c>GET api/me/notifications/toast-delta</c> feed into
/// Toast presentations (<see cref="IToastService"/>) while the operator session is authenticated.
/// </summary>
public interface INotificationToastWatcher : IDisposable
{
    /// <summary>Unread count from the most recent successful poll; 0 while not polling.</summary>
    int UnreadCount { get; }

    event Action? Changed;

    /// <summary>
    /// Arms the watcher. Idempotent: a second call is a no-op. Safe to call before the session is known
    /// -- polling begins once (and whenever) the session becomes <c>Authenticated</c>.
    /// </summary>
    void Start();
}
