namespace Apperture.Persistence.SqlServer;

/// <summary>Single-row instance policy for management UI sign-in chooser (see Epic auth UX).</summary>
public sealed class InstanceAuthUiPolicyEntity
{
    /// <summary>Fixed key; only one row is used per database.</summary>
    public string Id { get; set; } = "instance";

    public bool ShowLocalSignInInPrimaryChooser { get; set; } = true;

    public bool PreferEntraInChooser { get; set; }

    public bool AutoRedirectToEntra { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
