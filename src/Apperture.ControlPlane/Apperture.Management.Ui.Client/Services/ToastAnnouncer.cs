using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>What a screen reader should hear for one toast, and how urgently.</summary>
public sealed record ToastAnnouncement(Guid Id, bool Assertive, string Text);

/// <summary>
/// Decides which toasts still need announcing so each is announced exactly once, however often the
/// host re-renders. A toast that is already leaving, or older than
/// <see cref="ToastPolicy.AnnouncementLifetime"/> the first time it is seen (e.g. the host was remounted
/// by a layout swap after a previous host announced it), is treated as already announced.
/// </summary>
public sealed class ToastAnnouncer
{
    private readonly HashSet<Guid> _seen = new();

    /// <summary>Ids currently remembered (tests use this to prove removed toasts are forgotten).</summary>
    public int TrackedCount => _seen.Count;

    public IReadOnlyList<ToastAnnouncement> Reconcile(IReadOnlyCollection<ToastItem> toasts, DateTimeOffset now)
    {
        var live = toasts.Select(t => t.Id).ToHashSet();
        _seen.RemoveWhere(id => !live.Contains(id));

        var announcements = new List<ToastAnnouncement>();
        foreach (var toast in toasts)
        {
            if (toast.Phase != ToastPhase.Visible || !_seen.Add(toast.Id))
                continue;
            if (now - toast.CreatedAt > ToastPolicy.AnnouncementLifetime)
                continue;

            announcements.Add(new ToastAnnouncement(
                toast.Id,
                ToastPolicy.ResolveAriaLive(toast.Severity) == "assertive",
                ToastPolicy.BuildAnnouncement(toast.Severity, toast.Message, toast.Title)));
        }

        return announcements;
    }
}
