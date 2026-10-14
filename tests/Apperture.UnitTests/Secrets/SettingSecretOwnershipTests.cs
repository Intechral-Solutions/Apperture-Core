using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.3 (P-18, Q-3): the one definition of which secret a setting owns. An instance setting K owns provider
/// <c>platform-setting</c> / name <c>platform-setting:{K}</c> (the pre-existing identity, so the SMTP password stays valid); an
/// integration setting K of integration I owns provider <c>integration-setting</c> / name <c>integration-setting:{I}:{K}</c>.
/// Neither integration ids nor setting keys are character-validated, so the two components are escaped to keep the name
/// unambiguous and reversible.
/// </summary>
public sealed class SettingSecretOwnershipTests
{
    [Fact]
    public void An_instance_setting_keeps_the_identity_it_has_always_had()
    {
        var owner = SettingSecretOwnership.ForPlatformSetting("Notifications.Email.SmtpPasswordRef");

        owner.Provider.Should().Be("platform-setting");
        owner.Name.Should().Be("platform-setting:Notifications.Email.SmtpPasswordRef");
    }

    [Fact]
    public void An_integration_setting_has_its_own_provider_and_a_name_that_carries_the_integration_and_the_key()
    {
        var owner = SettingSecretOwnership.ForIntegrationSetting("0123456789abcdef01234567", "Sample.Api.Token");

        owner.Provider.Should().Be("integration-setting");
        owner.Name.Should().Be("integration-setting:0123456789abcdef01234567:Sample.Api.Token");
    }

    [Fact]
    public void Two_integrations_that_set_the_same_key_own_different_secrets()
    {
        var a = SettingSecretOwnership.ForIntegrationSetting("int-a", "Token");
        var b = SettingSecretOwnership.ForIntegrationSetting("int-b", "Token");

        a.Matches(b.Provider, b.Name).Should().BeFalse();
        b.Matches(a.Provider, a.Name).Should().BeFalse();
    }

    [Fact]
    public void An_instance_owner_and_an_integration_owner_never_match_each_other_even_for_the_same_key()
    {
        var instance = SettingSecretOwnership.ForPlatformSetting("Token");
        var integration = SettingSecretOwnership.ForIntegrationSetting("int-a", "Token");

        instance.Matches(integration.Provider, integration.Name).Should().BeFalse();
        integration.Matches(instance.Provider, instance.Name).Should().BeFalse();
    }

    [Fact]
    public void The_shape_an_integration_secret_had_before_is_the_instance_shape_and_is_never_an_integration_owner()
    {
        var legacy = SettingSecretOwnership.LegacyIntegrationSetting("Token");
        var integration = SettingSecretOwnership.ForIntegrationSetting("int-a", "Token");

        legacy.Provider.Should().Be("platform-setting");
        legacy.Name.Should().Be("platform-setting:Token");
        integration.Matches(legacy.Provider, legacy.Name).Should().BeFalse("a legacy secret never proves ownership by an integration");
    }

    [Theory]
    [InlineData("a:b", "c", "a", "b:c")]
    [InlineData("a", "b:c", "a:b", "c")]
    [InlineData("a%3Ab", "c", "a:b", "c")]
    [InlineData("a%", "3Ab:c", "a", "%3Ab:c")]
    [InlineData("a:", "b", "a", ":b")]
    public void A_delimiter_or_escape_character_in_an_id_or_a_key_never_makes_two_holders_share_an_owner(
        string idOne, string keyOne, string idTwo, string keyTwo)
    {
        var one = SettingSecretOwnership.ForIntegrationSetting(idOne, keyOne);
        var two = SettingSecretOwnership.ForIntegrationSetting(idTwo, keyTwo);

        one.Name.Should().NotBe(two.Name);
        one.Matches(two.Provider, two.Name).Should().BeFalse();
    }

    [Theory]
    [InlineData("0123456789abcdef01234567", "Sample.Api.Token")]
    [InlineData("a:b", "c")]
    [InlineData("a", "b:c")]
    [InlineData("100%", "key%3A")]
    [InlineData("::", "::")]
    [InlineData("int-a", "Key With Spaces")]
    public void An_integration_owner_name_reads_back_to_the_integration_and_the_key_it_was_built_from(string integrationId, string key)
    {
        var owner = SettingSecretOwnership.ForIntegrationSetting(integrationId, key);

        SettingSecretOwnership.TryParseIntegrationSettingName(owner.Name, out var id, out var parsedKey).Should().BeTrue();
        id.Should().Be(integrationId);
        parsedKey.Should().Be(key);
    }

    [Theory]
    [InlineData("platform-setting:Token")]
    [InlineData("integration-setting:only-one-part")]
    [InlineData("integration-setting:a:b:c")]
    [InlineData("integration-setting::key")]
    [InlineData("integration-setting:id:")]
    [InlineData("integration-setting:id:bad%escape")]
    [InlineData("integration-setting:id:bad%4")]
    [InlineData("Integration-Setting:id:key")]
    [InlineData("")]
    public void A_name_that_is_not_an_integration_setting_owner_name_does_not_parse(string name)
    {
        SettingSecretOwnership.TryParseIntegrationSettingName(name, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_name_that_would_not_fit_the_store_is_refused_rather_than_truncated()
    {
        // 64 colons escape to 192 characters; with a 128-character key of colons the name cannot fit in 256.
        var id = new string(':', 64);
        var key = new string(':', 128);

        var act = () => SettingSecretOwnership.ForIntegrationSetting(id, key);

        act.Should().Throw<ArgumentException>().WithMessage("*too long*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_blank_id_or_key_has_no_owner(string? blank)
    {
        ((Action)(() => SettingSecretOwnership.ForPlatformSetting(blank!))).Should().Throw<ArgumentException>();
        ((Action)(() => SettingSecretOwnership.ForIntegrationSetting(blank!, "key"))).Should().Throw<ArgumentException>();
        ((Action)(() => SettingSecretOwnership.ForIntegrationSetting("id", blank!))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_owner_is_compared_exactly_so_a_case_variant_is_not_the_owner()
    {
        var owner = SettingSecretOwnership.ForIntegrationSetting("int-a", "Token");

        owner.Matches("integration-setting", "integration-setting:INT-A:Token").Should().BeFalse();
        owner.Matches("Integration-Setting", owner.Name).Should().BeFalse();
    }
}
