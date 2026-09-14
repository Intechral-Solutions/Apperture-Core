namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Ensures only one top-bar utility menu (theme, notifications, account) is open at a time.
/// </summary>
public sealed class TopBarMenuCoordinator
{
    public static class Ids
    {
        public const string Theme = "theme";
        public const string Notifications = "notifications";
        public const string Account = "account";
        public const string View = "view";
    }

    public string? OpenMenuId { get; private set; }

    public event Action? Changed;

    public bool IsOpen(string menuId) =>
        string.Equals(OpenMenuId, menuId, StringComparison.Ordinal);

    public void Toggle(string menuId)
    {
        if (IsOpen(menuId))
            Close(menuId);
        else
            Open(menuId);
    }

    public void Open(string menuId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuId);
        if (IsOpen(menuId))
            return;

        OpenMenuId = menuId;
        Changed?.Invoke();
    }

    public void Close(string menuId)
    {
        if (!IsOpen(menuId))
            return;

        OpenMenuId = null;
        Changed?.Invoke();
    }

    public void CloseAll()
    {
        if (OpenMenuId is null)
            return;

        OpenMenuId = null;
        Changed?.Invoke();
    }
}
