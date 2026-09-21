using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Where a toast is in its life. Requesting dismissal and actual removal are separate steps.</summary>
public enum ToastPhase
{
    /// <summary>On screen and, for auto-dismissing toasts, counting down.</summary>
    Visible,

    /// <summary>Dismissal was requested; the exit animation is running. Removed after <see cref="ToastPolicy.ExitDuration"/>.</summary>
    Exiting
}

/// <summary>How an exiting toast leaves. Swipes continue in the direction the operator dragged.</summary>
public enum ToastExit
{
    /// <summary>Fade and drift slightly upward (auto-dismiss and the close button).</summary>
    Fade,
    SwipeLeft,
    SwipeRight
}

/// <summary>
/// Why the stack's dismissal countdowns are paused. Holds are stack-wide: while the operator is
/// interacting with the stack in any way, nothing in it expires or shifts under them.
/// </summary>
[Flags]
public enum ToastHold
{
    None = 0,

    /// <summary>A mouse is over the stack, or a touch is down on it.</summary>
    Pointer = 1,

    /// <summary>Keyboard focus is inside the stack.</summary>
    Focus = 2,

    /// <summary>A horizontal swipe is in progress.</summary>
    Drag = 4,

    /// <summary>The stack is being scrolled (including touch momentum).</summary>
    Scroll = 8,

    /// <summary>The browser tab is hidden, so the toast cannot be read.</summary>
    Hidden = 16
}

/// <summary><c>Duration</c> is null for toasts that persist until dismissed (errors).</summary>
public sealed record ToastItem(
    Guid Id,
    ToastSeverity Severity,
    string Message,
    string? Title,
    TimeSpan? Duration,
    ToastPhase Phase,
    ToastExit Exit,
    DateTimeOffset CreatedAt);
