using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class SensitivePlatformSettingUiHelpersTests
{
    [Fact]
    public void IsSecret_recognizes_input_kind_and_flag()
    {
        PlatformSettingUiHelpers.IsSecret(PlatformSettingUiHelpers.InputKindSecret).Should().BeTrue();
        PlatformSettingUiHelpers.IsSecret("Text", isSensitive: true).Should().BeTrue();
        PlatformSettingUiHelpers.IsSecret("Text").Should().BeFalse();
    }

    [Fact]
    public void Secret_status_labels_are_operator_safe()
    {
        PlatformSettingUiHelpers.GetSecretConfiguredLabel(true).Should().Be("Configured");
        PlatformSettingUiHelpers.GetSecretConfiguredLabel(false).Should().Be("Not configured");
    }

    [Fact]
    public void Smtp_password_help_mentions_secret_store_not_guid_pasting()
    {
        var note = PlatformSettingUiHelpers.TryGetSafetyNote("Notifications.Email.SmtpPasswordRef");
        note.Should().NotBeNull();
        note.Should().Contain("secret store");
        note.Should().NotContain("paste");
    }

    [Fact]
    public void PlatformSettings_page_uses_secret_replace_and_clear()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(
            dir!.FullName,
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Config", "PlatformSettings.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("SecretOperationReplace");
        content.Should().Contain("SecretOperationClear");
        content.Should().Contain("OnClear");
        content.Should().Contain("Sensitive settings store passwords");
    }

    [Fact]
    public void Update_request_supports_secret_mutation_fields()
    {
        var request = new UpdatePlatformSettingRequest(
            SecretOperation: PlatformSettingUiHelpers.SecretOperationReplace,
            SecretValue: "temp");
        request.SecretOperation.Should().Be("replace");
        request.Value.Should().BeNull();
    }
}
