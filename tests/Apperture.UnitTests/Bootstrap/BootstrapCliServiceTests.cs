using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using Apperture.Cli.Bootstrap;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

/// <summary>
/// Unit tests for BootstrapCliService: routing, safe output (no secrets).
/// </summary>
public sealed class BootstrapCliServiceTests
{
    private static BootstrapCliService CreateService(
        IBootstrapConfigurationStore? configStore = null,
        IBootstrapConfigurationValidator? validator = null,
        IBootstrapConnectionProfile? connectionProfile = null,
        IBootstrapCredentialManager? credentialManager = null,
        ISqlConnectionBootstrapProfileManager? profileManager = null,
        IBootstrapRecoveryService? recoveryService = null,
        IBootstrapAuditWriter? auditWriter = null)
    {
        var configStoreMock = configStore != null
            ? (Mock<IBootstrapConfigurationStore>?)null
            : new Mock<IBootstrapConfigurationStore>();
        var validatorMock = validator != null
            ? (Mock<IBootstrapConfigurationValidator>?)null
            : new Mock<IBootstrapConfigurationValidator>();
        var connectionProfileMock = connectionProfile != null
            ? (Mock<IBootstrapConnectionProfile>?)null
            : new Mock<IBootstrapConnectionProfile>();
        var credentialManagerMock = credentialManager != null
            ? (Mock<IBootstrapCredentialManager>?)null
            : new Mock<IBootstrapCredentialManager>();
        var profileManagerMock = profileManager != null
            ? (Mock<ISqlConnectionBootstrapProfileManager>?)null
            : new Mock<ISqlConnectionBootstrapProfileManager>();
        var recoveryServiceMock = recoveryService != null
            ? (Mock<IBootstrapRecoveryService>?)null
            : new Mock<IBootstrapRecoveryService>();
        var auditWriterMock = auditWriter != null
            ? (Mock<IBootstrapAuditWriter>?)null
            : new Mock<IBootstrapAuditWriter>();

        return new BootstrapCliService(
            credentialManager ?? credentialManagerMock!.Object,
            profileManager ?? profileManagerMock!.Object,
            connectionProfile ?? connectionProfileMock!.Object,
            validator ?? validatorMock!.Object,
            recoveryService ?? recoveryServiceMock!.Object,
            auditWriter ?? auditWriterMock!.Object,
            configStore ?? configStoreMock!.Object);
    }

    [Fact]
    public async Task ShowAsync_returns_non_secret_data()
    {
        var doc = new BootstrapConfigurationDocument
        {
            Profile = new SqlConnectionBootstrapProfile
            {
                Server = "localhost",
                Database = "Apperture",
                AuthMode = BootstrapAuthMode.WindowsIntegrated
            },
            CredentialReference = new BootstrapCredentialReference
            {
                SourceType = BootstrapCredentialSourceType.WindowsIntegrated,
                Reference = null
            }
        };

        var configStore = new Mock<IBootstrapConfigurationStore>();
        configStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var service = CreateService(configStore: configStore.Object);
        var result = await service.ShowAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Server=");
        result.Message.Should().Contain("localhost");
        result.Message.Should().Contain("Database=");
        result.Message.Should().Contain("Apperture");
        result.Message.Should().Contain("AuthMode=");
        // No secrets: no password, no connection string
        result.Message.ToUpperInvariant().Should().NotContain("PASSWORD");
        result.Message.ToUpperInvariant().Should().NotContain("PWD=");
    }

    [Fact]
    public async Task ShowAsync_with_no_config_returns_safe_message()
    {
        var configStore = new Mock<IBootstrapConfigurationStore>();
        configStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((BootstrapConfigurationDocument?)null);

        var service = CreateService(configStore: configStore.Object);
        var result = await service.ShowAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("No bootstrap configuration found.");
    }

    [Fact]
    public async Task ShowAsync_with_real_stub_config_store_does_not_throw()
    {
        var configStore = new Apperture.Bootstrap.Stubs.StubBootstrapConfigurationStore();
        var service = CreateService(configStore);
        var result = await service.ShowAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("No bootstrap configuration found.");
    }

    [Fact]
    public async Task ValidateAsync_when_no_config_returns_failure_with_message()
    {
        var configStore = new Mock<IBootstrapConfigurationStore>();
        configStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((BootstrapConfigurationDocument?)null);

        var service = CreateService(configStore: configStore.Object);
        var result = await service.ValidateAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("No configuration document found.");
    }

    [Fact]
    public async Task ValidateAsync_returns_BootstrapValidationResult()
    {
        var doc = new BootstrapConfigurationDocument
        {
            Profile = new SqlConnectionBootstrapProfile { Server = "localhost", Database = "Apperture", AuthMode = BootstrapAuthMode.WindowsIntegrated },
            CredentialReference = null
        };
        var validationResult = new BootstrapValidationResult { Success = true, Errors = [] };

        var configStore = new Mock<IBootstrapConfigurationStore>();
        configStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var validator = new Mock<IBootstrapConfigurationValidator>();
        validator.Setup(x => x.ValidateAsync(It.IsAny<BootstrapConfigurationDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var service = CreateService(configStore: configStore.Object, validator: validator.Object);
        var result = await service.ValidateAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task SetPasswordAsync_returnsDeferredMessage_withoutCallingCredentialManager()
    {
        const string secretPlaintext = "MySecretPassword123!";
        var credentialManager = new Mock<IBootstrapCredentialManager>();

        var service = CreateService(credentialManager: credentialManager.Object);
        var result = await service.SetPasswordAsync(secretPlaintext);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("deferred");
        result.Message.Should().Contain("Epic 108b");
        result.Message.Should().NotContain(secretPlaintext);
        credentialManager.Verify(
            x => x.UpdateAsync(It.IsAny<BootstrapUpdateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TestSqlConnectionAsync_success_output_contains_no_connection_string()
    {
        var connectionProfile = new Mock<IBootstrapConnectionProfile>();
        connectionProfile.Setup(x => x.ValidateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = CreateService(connectionProfile: connectionProfile.Object);
        var result = await service.TestSqlConnectionAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("SQL connection test succeeded.");
        result.Message.ToUpperInvariant().Should().NotContain("DATA SOURCE=");
        result.Message.ToUpperInvariant().Should().NotContain("PASSWORD=");
    }

    [Fact]
    public async Task TestSqlConnectionAsync_failure_output_contains_no_inner_secrets()
    {
        var connectionProfile = new Mock<IBootstrapConnectionProfile>();
        connectionProfile.Setup(x => x.ValidateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed."));

        var service = CreateService(connectionProfile: connectionProfile.Object);
        var result = await service.TestSqlConnectionAsync();

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Connection failed");
        // Ensure we don't leak connection string or password in error message
        result.Message.ToUpperInvariant().Should().NotContain("PASSWORD=");
    }

    [Fact]
    public async Task RepairAsync_does_not_call_RecoveryService()
    {
        var recoveryServiceMock = new Mock<IBootstrapRecoveryService>();
        var credentialManager = new Mock<IBootstrapCredentialManager>();
        credentialManager
            .Setup(x => x.ValidateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapValidationResult { Success = true, Errors = [] });

        var service = CreateService(credentialManager: credentialManager.Object, recoveryService: recoveryServiceMock.Object);
        await service.RepairAsync();

        recoveryServiceMock.Verify(x => x.ExecuteRecoveryAsync(It.IsAny<BootstrapRecoveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoveryUnlockAsync_calls_RecoveryService_returns_result()
    {
        var request = new BootstrapRecoveryRequest
        {
            ActionType = BootstrapRecoveryAction.RebindCredentialSource,
            Reason = "Test recovery"
        };
        var recoveryResult = new BootstrapRecoveryResult { Success = true, Message = "Recovery completed." };
        var recoveryServiceMock = new Mock<IBootstrapRecoveryService>();
        recoveryServiceMock
            .Setup(x => x.ExecuteRecoveryAsync(It.IsAny<BootstrapRecoveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recoveryResult);

        var service = CreateService(recoveryService: recoveryServiceMock.Object);
        var result = await service.RecoveryUnlockAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Recovery completed.");
        recoveryServiceMock.Verify(x => x.ExecuteRecoveryAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoveryUnlockAsync_when_denied_returns_failure()
    {
        var request = new BootstrapRecoveryRequest { ActionType = BootstrapRecoveryAction.RestorePlatformAccess, Reason = "Test" };
        var recoveryServiceMock = new Mock<IBootstrapRecoveryService>();
        recoveryServiceMock
            .Setup(x => x.ExecuteRecoveryAsync(It.IsAny<BootstrapRecoveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapRecoveryResult { Success = false, Message = "Recovery not authorized." });

        var service = CreateService(recoveryService: recoveryServiceMock.Object);
        var result = await service.RecoveryUnlockAsync(request);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Recovery not authorized.");
    }
}
