using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// SEC-2.3 (P-19, A3.4): the setting-secret boundary. A reference read from a setting row is resolved only through
/// <c>ResolveOwnedAsync</c> against the owner the setting itself defines, so a reference to another holder's secret is refused
/// before that secret's payload is read. Every refusal is one typed exception with a fixed message; one top-level operation writes
/// one W3 row; and the refusal never waits on, or is changed by, the evidence. The one unchecked path is the explicit
/// deployment-configured one (P-22).
/// </summary>
public sealed class SettingSecretResolverTests
{
    private const string Reference = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11";
    private const string IntegrationId = "int-a";
    private const string Key = "Sample.Api.Token";
    private const string PlatformKey = "Notifications.Email.SmtpPasswordRef";

    private static ResolvedSecret Resolved(string value) =>
        new(new SecretRef(Reference), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: value));

    private sealed class Rig
    {
        /// <summary>Strict: every call without a setup fails the test. The unchecked <c>ResolveAsync</c> has none, so calling it is a failure.</summary>
        public Mock<ISecretResolver> Secrets { get; } = new(MockBehavior.Strict);

        public Mock<IAuditEventWriter> Audit { get; } = new();
        public List<AuditEvent> Rows { get; } = [];
        public List<AuditActor> Actors { get; } = [];
        public List<string> Logs { get; } = [];
        public Mock<IPrincipalContext> Principals { get; } = new();
        public SettingSecretResolver Sut { get; private set; } = null!;

        public static Rig New(bool auditWrites = true, Exception? auditThrows = null, bool authenticated = true, bool withIntegrations = true)
        {
            var rig = new Rig();
            rig.Audit.Setup(a => a.TryWriteAsync(It.IsAny<AuditEvent>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()))
                .Returns((AuditEvent e, AuditActor actor, CancellationToken _) =>
                {
                    rig.Rows.Add(e);
                    rig.Actors.Add(actor);
                    return auditThrows is not null ? Task.FromException<bool>(auditThrows) : Task.FromResult(auditWrites);
                });
            rig.Principals.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(authenticated ? Mock.Of<IActor>() : null);

            var integrations = new Mock<IIntegrationRepository>();
            integrations.Setup(i => i.GetByIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DomainIntegration(new IntegrationId(IntegrationId), "Integration", "mod", null, IntegrationState.Active, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                {
                    ResourceContextId = new ResourceContextId("ctx-1"),
                });
            rig.Sut = new SettingSecretResolver(
                rig.Secrets.Object, rig.Audit.Object, new CapturingLogger(rig.Logs), withIntegrations ? integrations.Object : null, rig.Principals.Object);
            return rig;
        }

        public void IntegrationOwned(string value) =>
            Secrets.Setup(s => s.ResolveOwnedAsync(
                    It.Is<SecretRef>(r => r.Value == Reference),
                    It.Is<SecretOwner>(o => o.Provider == "integration-setting" && o.Name == $"integration-setting:{IntegrationId}:{Key}"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Resolved(value));

        public void PlatformOwned(string value) =>
            Secrets.Setup(s => s.ResolveOwnedAsync(
                    It.Is<SecretRef>(r => r.Value == Reference),
                    It.Is<SecretOwner>(o => o.Provider == "platform-setting" && o.Name == $"platform-setting:{PlatformKey}"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Resolved(value));

        public void Refuses() =>
            Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretUnavailableException());
    }

    private sealed class CapturingLogger(List<string> sink) : ILogger<SettingSecretResolver>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
    }

    // ---- the owned path ----

    [Fact]
    public async Task An_integration_setting_resolves_through_the_owned_call_with_the_owner_the_setting_defines()
    {
        var rig = Rig.New();
        rig.IntegrationOwned("s3cret");

        var value = await rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run);

        value.Should().Be("s3cret");
        rig.Rows.Should().BeEmpty("a successful resolution is not audited");
        rig.Secrets.VerifyAll();
    }

    [Fact]
    public async Task An_instance_setting_resolves_through_the_owned_call_with_the_instance_owner()
    {
        var rig = Rig.New();
        rig.PlatformOwned("smtp-pass");

        var value = await rig.Sut.ResolvePlatformSettingAsync(PlatformKey, Reference, SettingSecretResolutionPath.Management);

        value.Should().Be("smtp-pass");
        rig.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task The_material_selection_matches_the_connection_boundary_json_is_used_when_there_is_no_opaque_string()
    {
        var rig = Rig.New();
        rig.Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(Reference), new SecretMaterial(SecretMaterialType.Json, Json: "{\"k\":1}")));

        (await rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run))
            .Should().Be("{\"k\":1}");
    }

    // ---- refusal ----

    [Fact]
    public async Task A_refused_integration_setting_throws_the_fixed_exception_and_writes_one_row_for_the_worker()
    {
        var rig = Rig.New();
        rig.Refuses();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run).AsTask();

        var thrown = (await act.Should().ThrowAsync<SettingSecretUnavailableException>()).Which;
        thrown.SettingKey.Should().Be(Key);
        var row = rig.Rows.Should().ContainSingle().Which;
        row.Action.Should().Be("integration.settings.secret.resolution_refused");
        row.Target!.Type.Should().Be("integration_setting");
        row.Target.Id.Should().Be($"{IntegrationId}:{Key}");
        row.Fields["path"].Should().Be("run");
        row.Related.ResourceContextId.Should().Be("ctx-1");
        rig.Actors.Single().Should().BeSameAs(AuditActor.Worker);
    }

    [Fact]
    public async Task A_refused_integration_setting_on_the_management_path_is_attributed_to_the_signed_in_operator()
    {
        var rig = Rig.New();
        rig.Refuses();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Management).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>();
        rig.Rows.Single().Fields["path"].Should().Be("management");
        rig.Actors.Single().Should().BeSameAs(AuditActor.CurrentPrincipal);
    }

    [Fact]
    public async Task A_refusal_on_the_management_path_with_no_signed_in_principal_is_attributed_to_an_anonymous_caller_not_to_nobody()
    {
        var rig = Rig.New(authenticated: false);
        rig.Refuses();

        var act = () => rig.Sut.ResolvePlatformSettingAsync(PlatformKey, Reference, SettingSecretResolutionPath.Management).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>();
        rig.Actors.Single().Should().BeSameAs(AuditActor.Anonymous);
    }

    [Fact]
    public async Task A_refused_instance_setting_writes_its_own_action_and_a_run_refusal_is_attributed_to_the_platform_not_to_a_person()
    {
        var rig = Rig.New();
        rig.Refuses();

        var act = () => rig.Sut.ResolvePlatformSettingAsync(PlatformKey, Reference, SettingSecretResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>();
        var row = rig.Rows.Should().ContainSingle().Which;
        row.Action.Should().Be("platform.settings.secret.resolution_refused");
        row.Target!.Type.Should().Be("platform_setting");
        row.Target.Id.Should().Be(PlatformKey);
        row.Fields["path"].Should().Be("run");
        rig.Actors.Single().Kind.Should().Be(AuditActorKinds.System);
        rig.Actors.Single().Literal.Should().StartWith("system:");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reference_is_a_refusal_without_asking_the_secret_resolver(string? blank)
    {
        var rig = Rig.New();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, blank, SettingSecretResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>();
        rig.Rows.Should().ContainSingle();
        rig.Secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failure_that_is_not_a_refusal_propagates_and_writes_no_refusal_row()
    {
        var rig = Rig.New();
        rig.Secrets.Setup(s => s.ResolveOwnedAsync(It.IsAny<SecretRef>(), It.IsAny<SecretOwner>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("protector unavailable"));

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<TimeoutException>();
        rig.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("reports false")]
    [InlineData("throws")]
    public async Task A_refusal_stands_whether_or_not_its_evidence_can_be_written(string failure)
    {
        var rig = failure == "reports false"
            ? Rig.New(auditWrites: false)
            : Rig.New(auditThrows: new InvalidOperationException("db down"));
        rig.Refuses();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>("the refusal was decided before the evidence was attempted");
    }

    [Fact]
    public async Task A_missing_integration_repository_only_leaves_the_related_context_out()
    {
        var rig = Rig.New(withIntegrations: false);
        rig.Refuses();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run).AsTask();

        await act.Should().ThrowAsync<SettingSecretUnavailableException>();
        rig.Rows.Single().Related.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public async Task Nothing_the_refusal_exposes_names_the_reference_the_owner_or_the_integration_s_secret_name()
    {
        var rig = Rig.New();
        rig.Refuses();

        var act = () => rig.Sut.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run).AsTask();

        var thrown = (await act.Should().ThrowAsync<SettingSecretUnavailableException>()).Which;
        var texts = new[] { thrown.Message, thrown.ToString() }.Concat(rig.Logs).Concat(rig.Rows.SelectMany(r => r.Fields.Values.Select(v => v?.ToString() ?? "")));
        foreach (var text in texts)
        {
            text.Should().NotContain(Reference);
            text.Should().NotContain("integration-setting:");
            text.Should().NotContain("platform-setting:");
        }
    }

    // ---- the one explicit trusted path ----

    [Fact]
    public async Task A_deployment_configured_reference_is_resolved_by_reference_alone_and_writes_no_refusal_row()
    {
        var rig = Rig.New();
        rig.Secrets.Setup(s => s.ResolveAsync(It.Is<SecretRef>(r => r.Value == Reference), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved("configured-secret"));

        var value = await rig.Sut.ResolveDeploymentConfiguredAsync(Reference);

        value.Should().Be("configured-secret");
        rig.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_deployment_configured_failure_propagates_unchanged_so_the_caller_keeps_its_existing_handling()
    {
        var rig = Rig.New();
        rig.Secrets.Setup(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecretUnavailableException());

        var act = () => rig.Sut.ResolveDeploymentConfiguredAsync(Reference).AsTask();

        await act.Should().ThrowAsync<SecretUnavailableException>();
        rig.Rows.Should().BeEmpty();
    }
}
