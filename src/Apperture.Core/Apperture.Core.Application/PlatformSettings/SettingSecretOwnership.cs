using System.Text;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// The one definition of which secret a setting owns. The store holds the secret of an instance setting <c>K</c> under provider
/// <c>platform-setting</c> with the name <c>platform-setting:{K}</c>, and the secret of setting <c>K</c> of integration <c>I</c>
/// under provider <c>integration-setting</c> with the name <c>integration-setting:{I}:{K}</c>. The rule that creates a setting's
/// secret, the rule that proves a reference is the setting's own before it is versioned or disabled, and the rule every read
/// applies before material is returned all come from here, so write, read, reset and retirement cannot drift apart.
/// </summary>
/// <remarks>
/// <para>A well-formed GUID in a setting row is not proof of ownership. The proof is the secret's own stored descriptor, compared
/// exactly (ordinal) with the owner this class derives from the holder.</para>
/// <para><b>Delimiter safety.</b> Neither integration ids nor setting keys are character-validated, so either may contain
/// <c>:</c>. Each is therefore escaped before it is joined (<c>%</c> becomes <c>%25</c> and <c>:</c> becomes <c>%3A</c>), which
/// makes the name unambiguous and reversible (<see cref="TryParseIntegrationSettingName"/>). An id or key that holds neither
/// character, which is every id the platform generates and every key a module declares today, has exactly the name the approved
/// format spells. The instance name is not escaped: an instance key is unique on its own, and the existing identity (the SMTP
/// password's) must stay valid.</para>
/// <para><b>Legacy.</b> Before SEC-2.3 an integration setting's secret was stored under the instance shape
/// (<c>platform-setting:{K}</c>), which cannot say which integration made it. <see cref="LegacyIntegrationSetting"/> names that
/// shape. It never proves ownership by an integration.</para>
/// </remarks>
public static class SettingSecretOwnership
{
    /// <summary>The provider of the secret an instance setting owns (and of the legacy integration shape).</summary>
    public const string PlatformProvider = "platform-setting";

    /// <summary>The provider of the secret an integration setting owns.</summary>
    public const string IntegrationProvider = "integration-setting";

    /// <summary>The widest descriptor name the secret store holds.</summary>
    public const int MaxNameLength = 256;

    private const string PlatformPrefix = "platform-setting:";
    private const string IntegrationPrefix = "integration-setting:";

    /// <summary>The owner of the secret that instance setting <paramref name="key"/> may use.</summary>
    public static SecretOwner ForPlatformSetting(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new SecretOwner(PlatformProvider, PlatformPrefix + key);
    }

    /// <summary>The owner of the secret that setting <paramref name="key"/> of integration <paramref name="integrationId"/> may use.</summary>
    /// <exception cref="ArgumentException">The name would not fit the secret store; it is refused rather than truncated.</exception>
    public static SecretOwner ForIntegrationSetting(string integrationId, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(integrationId);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var name = IntegrationPrefix + Escape(integrationId) + ":" + Escape(key);
        if (name.Length > MaxNameLength)
            throw new ArgumentException("The integration setting's secret name is too long for the secret store.", nameof(key));
        return new SecretOwner(IntegrationProvider, name);
    }

    /// <summary>The shape an integration setting's secret had before integration-specific names: the instance shape for the same key.</summary>
    public static SecretOwner LegacyIntegrationSetting(string key) => ForPlatformSetting(key);

    /// <summary>
    /// Whether <paramref name="name"/> has the shape of a name the settings lifecycle gives a secret (an instance, legacy or
    /// integration name), whichever setting it names. It says nothing about who owns the secret.
    /// </summary>
    public static bool HasSettingSecretNameShape(string? name) =>
        name is not null
        && (name.StartsWith(PlatformPrefix, StringComparison.Ordinal) || name.StartsWith(IntegrationPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Reads an integration owner name back to the integration id and setting key it was built from. False for any other name,
    /// including the legacy shape, a name with the wrong number of parts and one with a malformed escape.
    /// </summary>
    public static bool TryParseIntegrationSettingName(string? name, out string integrationId, out string key)
    {
        integrationId = string.Empty;
        key = string.Empty;
        if (name is null || !name.StartsWith(IntegrationPrefix, StringComparison.Ordinal))
            return false;

        var parts = name[IntegrationPrefix.Length..].Split(':');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return false;
        if (!TryUnescape(parts[0], out var id) || !TryUnescape(parts[1], out var parsedKey))
            return false;

        integrationId = id;
        key = parsedKey;
        return true;
    }

    private static string Escape(string component)
    {
        if (component.IndexOfAny(['%', ':']) < 0)
            return component;

        var builder = new StringBuilder(component.Length + 8);
        foreach (var c in component)
        {
            switch (c)
            {
                case '%': builder.Append("%25"); break;
                case ':': builder.Append("%3A"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    private static bool TryUnescape(string component, out string value)
    {
        value = string.Empty;
        var builder = new StringBuilder(component.Length);
        for (var i = 0; i < component.Length; i++)
        {
            var c = component[i];
            if (c != '%')
            {
                builder.Append(c);
                continue;
            }

            // An escape is the percent sign and two more characters.
            if (i + 2 >= component.Length)
                return false;

            var escape = component.AsSpan(i + 1, 2);
            if (escape.SequenceEqual("25"))
                builder.Append('%');
            else if (escape.SequenceEqual("3A"))
                builder.Append(':');
            else
                return false;
            i += 2;
        }

        value = builder.ToString();
        return true;
    }
}
