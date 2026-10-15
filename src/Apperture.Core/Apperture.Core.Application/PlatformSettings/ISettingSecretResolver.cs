namespace Apperture.Core.Application.PlatformSettings;

/// <summary>What kind of operation asked for a setting's secret. It is recorded on a refusal and decides who is named as the actor.</summary>
public enum SettingSecretResolutionPath
{
    /// <summary>A step or a background service uses the secret with no operator involved.</summary>
    Run,

    /// <summary>A request handler resolves it for the signed-in caller, or for a caller who is not signed in (a recovery flow).</summary>
    Management,
}

/// <summary>
/// The only way a secret reference read from a setting row becomes material. It proves the reference is the setting's own, by the
/// secret's own stored descriptor, before the secret's payload is read or unprotected, and refuses everything else identically.
/// A holder that takes a reference from a setting row (the runtime accessor, the SMTP password) calls it instead of
/// <c>ISecretResolver</c>, which proves nothing about ownership.
/// </summary>
public interface ISettingSecretResolver
{
    /// <summary>
    /// Resolves the reference stored in the row of setting <paramref name="key"/> of integration <paramref name="integrationId"/>,
    /// only if it names a secret that setting owns (<see cref="SettingSecretOwnership.ForIntegrationSetting"/>).
    /// </summary>
    /// <returns>The secret's text.</returns>
    /// <exception cref="SettingSecretUnavailableException">
    /// The reference is blank, unreadable, names no usable secret, or names one the setting does not own. One refusal row is
    /// written (best effort), whatever the reason, and the secret is not read.
    /// </exception>
    ValueTask<string> ResolveIntegrationSettingAsync(
        string integrationId, string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="ResolveIntegrationSettingAsync"/>, for an instance setting (<see cref="SettingSecretOwnership.ForPlatformSetting"/>).</summary>
    ValueTask<string> ResolvePlatformSettingAsync(
        string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a reference that deployment configuration supplied (<c>Notifications:Email:SmtpPasswordRef</c> in an application
    /// settings file or the environment). It is not a setting row, so the row ownership rule does not apply (P-22): deployment
    /// configuration is trusted input, as it is for every other configured value, and the reference alone decides which secret is
    /// returned. This is the one deliberately unchecked path; it is named so that using it is visible, and a row-sourced reference
    /// must never be passed to it.
    /// </summary>
    ValueTask<string> ResolveDeploymentConfiguredAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// A setting's secret was refused. The same exception and the same fixed message for every reason (a blank or unreadable
/// reference, a missing, disabled or never-activated secret, another holder's secret): the message names no reference, owner or
/// condition. <see cref="SettingKey"/> is the setting's key, which the caller already holds.
/// </summary>
public sealed class SettingSecretUnavailableException : InvalidOperationException
{
    public SettingSecretUnavailableException(string settingKey)
        : base("The secret for the setting is not available.")
    {
        SettingKey = settingKey;
    }

    /// <summary>The key of the setting whose secret was refused.</summary>
    public string SettingKey { get; }
}
