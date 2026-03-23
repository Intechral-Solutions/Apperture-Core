namespace Apperture.Management.Ui.Shared.Options;

/// <summary>Epic 42 shell: sign-in mode and management API host for OIDC redirects.</summary>
public sealed class ShellUiOptions
{
    public const string SectionName = "ShellUi";

    /// <summary>When true, primary sign-in uses full navigation to management host <c>/signin</c> (Entra).</summary>
    public bool UseEntraSignIn { get; set; }

    /// <summary>Optional absolute base URL of the management host (e.g. <c>http://localhost:9000</c>). Used for /signin and /signout when the UI is on another origin.</summary>
    public string? ManagementHostUrl { get; set; }
}
