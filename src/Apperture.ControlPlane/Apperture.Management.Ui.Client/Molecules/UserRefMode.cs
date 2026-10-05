namespace Apperture.Management.Ui.Client.Molecules;

/// <summary>How <see cref="UserRef"/> presents a resolved user.</summary>
public enum UserRefMode
{
    /// <summary>Tables, dense lists and inline mentions: the username is visible, the display name is in the accessible name.</summary>
    Compact = 0,

    /// <summary>Headers, detail pages and holder lists: the display name is primary, the username is visible secondary text.</summary>
    Rich = 1,
}
