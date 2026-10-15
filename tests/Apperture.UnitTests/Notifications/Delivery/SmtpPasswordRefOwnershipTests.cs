using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

/// <summary>
/// SEC-2.3 (A3.4, A3.8, P-19, P-22): the SMTP password is read through the setting-secret boundary when its reference came from the
/// setting row, so a row that names another holder's secret reads as "not configured" and sends no mail with a foreign credential.
/// A reference that deployment configuration supplied is not a setting row: it is trusted input outside the row ownership rule, and
/// that exception is pinned here.
/// </summary>
public sealed class SmtpPasswordRefOwnershipTests
{
    private const string Reference = "550e8400-e29b-41d4-a716-446655440000";
    private static readonly string Key = PlatformSettingKeys.NotificationsEmailSmtpPasswordRef;

    private static EmailSmtpCredentialResolver Resolver(Mock<ISettingSecretResolver> secrets) =>
        new(NullLogger<EmailSmtpCredentialResolver>.Instance, secrets.Object);

    // ---- the row-sourced reference ----

    [Fact]
    public async Task A_row_sourced_reference_is_resolved_through_the_boundary_for_the_instance_setting_and_never_unchecked()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolvePlatformSettingAsync(Key, Reference, SettingSecretResolutionPath.Run, It.IsAny<CancellationToken>()))
            .ReturnsAsync("smtp-secret");

        var password = await Resolver(secrets).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPasswordRef = Reference, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow });

        password.Should().Be("smtp-secret");
        secrets.VerifyAll();
    }

    [Fact]
    public async Task The_management_path_is_passed_through_so_account_mail_is_recorded_as_a_request()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolvePlatformSettingAsync(Key, Reference, SettingSecretResolutionPath.Management, It.IsAny<CancellationToken>()))
            .ReturnsAsync("smtp-secret");

        var password = await Resolver(secrets).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPasswordRef = Reference, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow },
            SettingSecretResolutionPath.Management);

        password.Should().Be("smtp-secret");
    }

    [Fact]
    public async Task A_row_sourced_reference_the_boundary_refuses_reads_as_no_password_and_nothing_unchecked_is_tried()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolvePlatformSettingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SettingSecretResolutionPath>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SettingSecretUnavailableException(Key));

        var password = await Resolver(secrets).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPasswordRef = Reference, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow });

        password.Should().BeNull("the setting reads as not configured, never as a foreign value");
        secrets.Verify(s => s.ResolveDeploymentConfiguredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- the deployment-configured reference (P-22) ----

    [Fact]
    public async Task A_reference_that_deployment_configuration_supplied_stays_outside_the_row_rule_and_keeps_working()
    {
        // Strict, with no setup for the owned call: the row rule must not be applied to it.
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolveDeploymentConfiguredAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync("configured-secret");

        var password = await Resolver(secrets).ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = Reference });

        password.Should().Be("configured-secret");
        new EmailNotificationOptions().SmtpPasswordRefSource.Should().Be(SmtpPasswordRefSource.Configuration, "configuration binding is the default source");
        secrets.VerifyAll();
    }

    [Fact]
    public async Task A_failure_to_resolve_a_configured_reference_is_the_existing_no_password_behaviour()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolveDeploymentConfiguredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecretUnavailableException());

        (await Resolver(secrets).ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = Reference })).Should().BeNull();
    }

    [Fact]
    public async Task A_blank_reference_and_no_boundary_are_both_no_password()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);

        (await Resolver(secrets).ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = "  " })).Should().BeNull();
        (await new EmailSmtpCredentialResolver(NullLogger<EmailSmtpCredentialResolver>.Instance)
            .ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = Reference })).Should().BeNull();
        secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_configured_plain_password_still_wins_and_asks_nobody()
    {
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);

        var password = await Resolver(secrets).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPassword = "from-config", SmtpPasswordRef = Reference, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow });

        password.Should().Be("from-config");
        secrets.VerifyNoOtherCalls();
    }

    // ---- where the reference came from is recorded where it is applied ----

    private sealed class StubSettings(PlatformSettingValueSource source, string? value) : IPlatformSettingsResolver
    {
        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformSettingResolvedValue>>([]);

        public Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default)
        {
            var def = new PlatformSettingDefinitionRegistry().TryGet(key)!;
            return Task.FromResult<PlatformSettingResolvedValue?>(
                PlatformSettingResolvedValueFactory.Create(def, value ?? string.Empty, source));
        }

        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(value);
    }

    private static EmailNotificationOptions PostConfigured(PlatformSettingValueSource source, string? value, EmailNotificationOptions? seed = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformSettingsResolver>(new StubSettings(source, value));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var options = seed ?? new EmailNotificationOptions();
        new EmailNotificationOptionsPlatformSettingsPostConfigure(scopeFactory, NullLogger<EmailNotificationOptionsPlatformSettingsPostConfigure>.Instance)
            .PostConfigure(null, options);
        return options;
    }

    [Fact]
    public void A_reference_overlaid_from_the_setting_row_is_marked_as_row_sourced()
    {
        var options = PostConfigured(PlatformSettingValueSource.Database, Reference);

        options.SmtpPasswordRef.Should().Be(Reference);
        options.SmtpPasswordRefSource.Should().Be(SmtpPasswordRefSource.SettingRow);
    }

    [Theory]
    [InlineData(PlatformSettingValueSource.ConfigurationDefault)]
    [InlineData(PlatformSettingValueSource.DefinitionDefault)]
    public void A_reference_that_is_not_from_the_setting_row_stays_configuration_sourced(PlatformSettingValueSource source)
    {
        var options = PostConfigured(source, Reference);

        options.SmtpPasswordRefSource.Should().Be(SmtpPasswordRefSource.Configuration);
    }

    [Fact]
    public void A_row_that_holds_nothing_leaves_the_configured_reference_and_its_source_alone()
    {
        var options = PostConfigured(
            PlatformSettingValueSource.Database, "  ",
            new EmailNotificationOptions { SmtpPasswordRef = "configured-ref" });

        options.SmtpPasswordRef.Should().Be("configured-ref");
        options.SmtpPasswordRefSource.Should().Be(SmtpPasswordRefSource.Configuration);
    }

    // ---- the overlay and the resolver agree, end to end ----

    [Fact]
    public async Task A_reference_the_overlay_took_from_the_row_is_checked_for_ownership_when_it_is_read()
    {
        var options = PostConfigured(PlatformSettingValueSource.Database, Reference);
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolvePlatformSettingAsync(Key, Reference, SettingSecretResolutionPath.Run, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SettingSecretUnavailableException(Key));

        var password = await Resolver(secrets).ResolvePasswordAsync(options);

        password.Should().BeNull("a row that names a secret it does not own reads as not configured");
        secrets.VerifyAll();
    }

    [Fact]
    public async Task A_reference_the_overlay_left_as_configured_is_not_subject_to_the_row_rule_when_it_is_read()
    {
        var options = PostConfigured(PlatformSettingValueSource.ConfigurationDefault, Reference);
        var secrets = new Mock<ISettingSecretResolver>(MockBehavior.Strict);
        secrets.Setup(s => s.ResolveDeploymentConfiguredAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync("configured-secret");

        (await Resolver(secrets).ResolvePasswordAsync(options)).Should().Be("configured-secret");
        secrets.VerifyAll();
    }

    // ---- a copy of the options keeps where the reference came from ----

    [Fact]
    public void The_account_mail_sender_copies_every_option_including_where_the_reference_came_from()
    {
        var clone = typeof(AccountSecurityMailSender)
            .GetMethod("CloneForSecurityMail", BindingFlags.NonPublic | BindingFlags.Static)!;
        var source = new EmailNotificationOptions
        {
            Enabled = false,
            SmtpHost = "smtp.test",
            SmtpPort = 2525,
            SmtpUseSsl = false,
            SmtpUser = "user",
            SmtpPassword = "pw",
            SmtpPasswordRef = Reference,
            SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow,
            FromAddress = "from@test",
            FromDisplayName = "Display",
        };

        var copy = (EmailNotificationOptions)clone.Invoke(null, [source])!;

        // Enabled is forced on for security mail; every other property is carried over, so a property added later cannot be
        // dropped silently (a dropped source would turn a row-sourced reference into a trusted one).
        foreach (var property in typeof(EmailNotificationOptions).GetProperties().Where(p => p.Name != nameof(EmailNotificationOptions.Enabled)))
            property.GetValue(copy).Should().Be(property.GetValue(source), $"{property.Name} must survive the copy");
        copy.Enabled.Should().BeTrue();
    }
}
