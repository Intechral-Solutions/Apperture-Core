using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class EmailNotificationOptionsTests
{
    [Fact]
    public void Platform_settings_overlay_applies_smtp_host()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformSettingsResolver>(new StubPlatformSettingsResolver(
            (PlatformSettingKeys.NotificationsEmailSmtpHost, "smtp.platform.test"),
            (PlatformSettingKeys.NotificationsEmailSmtpPort, 2525),
            (PlatformSettingKeys.NotificationsEmailFromAddress, "ops@platform.test")));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var sut = new EmailNotificationOptionsPlatformSettingsPostConfigure(
            scopeFactory,
            NullLogger<EmailNotificationOptionsPlatformSettingsPostConfigure>.Instance);
        var email = new EmailNotificationOptions();
        sut.PostConfigure(null, email);

        email.SmtpHost.Should().Be("smtp.platform.test");
        email.SmtpPort.Should().Be(2525);
        email.FromAddress.Should().Be("ops@platform.test");
    }

    [Fact]
    public async Task Credential_resolver_uses_plain_password_when_configured()
    {
        var sut = new EmailSmtpCredentialResolver(NullLogger<EmailSmtpCredentialResolver>.Instance);
        var password = await sut.ResolvePasswordAsync(new EmailNotificationOptions { SmtpPassword = "secret" });
        password.Should().Be("secret");
    }

    [Fact]
    public async Task Credential_resolver_uses_secret_ref_when_configured()
    {
        var secretId = "550e8400-e29b-41d4-a716-446655440000";
        var resolver = new StubSettingSecretResolver(secretId, "smtp-secret");
        var sut = new EmailSmtpCredentialResolver(NullLogger<EmailSmtpCredentialResolver>.Instance, resolver);
        var password = await sut.ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = secretId });
        password.Should().Be("smtp-secret");
    }

    [Fact]
    public async Task Credential_resolver_prefers_plain_password_over_ref()
    {
        var secretId = "550e8400-e29b-41d4-a716-446655440000";
        var resolver = new StubSettingSecretResolver(secretId, "from-ref");
        var sut = new EmailSmtpCredentialResolver(NullLogger<EmailSmtpCredentialResolver>.Instance, resolver);
        var password = await sut.ResolvePasswordAsync(new EmailNotificationOptions
        {
            SmtpPassword = "from-config",
            SmtpPasswordRef = secretId,
        });
        password.Should().Be("from-config");
    }

    [Fact]
    public async Task Credential_resolver_does_not_store_password_in_platform_settings_keys()
    {
        PlatformSettingKeys.NotificationsEmailSmtpPasswordRef.Should().Be("Notifications.Email.SmtpPasswordRef");
        var passwordDef = new PlatformSettingDefinitionRegistry().TryGet(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef)!;
        passwordDef.IsSensitive.Should().BeTrue();
        passwordDef.InputKind.Should().Be(PlatformSettingInputKind.Secret);
        new PlatformSettingDefinitionRegistry().GetAll()
            .Should().NotContain(d => d.Key == "Notifications.Email.SmtpPassword");
    }

    private sealed class StubPlatformSettingsResolver : IPlatformSettingsResolver
    {
        private readonly Dictionary<string, object?> _values;

        public StubPlatformSettingsResolver(params (string Key, object? Value)[] values) =>
            _values = values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformSettingResolvedValue>>([]);

        public Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlatformSettingResolvedValue?>(null);

        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
    }

    /// <summary>A reference supplied by deployment configuration is trusted: only the deployment-configured call resolves it.</summary>
    private sealed class StubSettingSecretResolver(string expectedId, string password) : ISettingSecretResolver
    {
        public ValueTask<string> ResolveDeploymentConfiguredAsync(string reference, CancellationToken cancellationToken = default)
        {
            reference.Should().Be(expectedId);
            return new ValueTask<string>(password);
        }

        public ValueTask<string> ResolveIntegrationSettingAsync(string integrationId, string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string> ResolvePlatformSettingAsync(string key, string? reference, SettingSecretResolutionPath path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
