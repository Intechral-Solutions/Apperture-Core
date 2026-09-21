using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

public interface IToastService
{
    IReadOnlyList<ToastItem> Toasts { get; }

    event Action? Changed;

    void Show(ToastSeverity severity, string message, string? title = null, TimeSpan? duration = null);

    /// <summary>Requests dismissal: the toast plays its exit animation and is then removed. Safe to call repeatedly.</summary>
    void Dismiss(Guid id);

    /// <summary>As <see cref="Dismiss(Guid)"/>, but the exit continues in the direction of a swipe.</summary>
    void Dismiss(Guid id, ToastExit exit);

    /// <summary>
    /// Presentation-side signal: pauses (<paramref name="active"/>) or resumes the stack's dismissal
    /// countdowns. Callers that only show toasts never use this; <c>ToastHost</c> does.
    /// </summary>
    void SetHold(ToastHold source, bool active);
}
