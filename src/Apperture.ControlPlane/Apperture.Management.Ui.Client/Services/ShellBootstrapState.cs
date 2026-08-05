namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// UIX-2: App finishes theme/session/TZ/sign-in-options before the gate mounts MainLayout.
/// Mounting the shell while <c>App.BootstrapAsync</c> is still running caused parentNode on first paint.
/// </summary>
public sealed class ShellBootstrapState
{
    public bool IsReady { get; private set; }

    public event Action? Changed;

    public void MarkReady()
    {
        if (IsReady)
            return;

        IsReady = true;
        Changed?.Invoke();
    }
}
