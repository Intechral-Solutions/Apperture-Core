using Apperture.Management.Ui.Shared;

namespace Apperture.Management.Ui.Client.Services;

public interface IToastService
{
    IReadOnlyList<ToastItem> Toasts { get; }

    event Action? Changed;

    void Show(ToastSeverity severity, string message, string? title = null, TimeSpan? duration = null);

    void Dismiss(Guid id);
}
