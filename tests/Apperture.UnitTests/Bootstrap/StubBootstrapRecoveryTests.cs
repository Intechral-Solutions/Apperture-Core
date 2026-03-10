using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using Apperture.Bootstrap.Stubs;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

/// <summary>
/// Stub behavior: StubBootstrapAuditWriter.WriteRecoveryAsync no-op; StubBootstrapRecoveryService authorizes then audits.
/// </summary>
public sealed class StubBootstrapRecoveryTests
{
    [Fact]
    public async Task StubBootstrapAuditWriter_WriteRecoveryAsync_does_not_throw()
    {
        var stub = new StubBootstrapAuditWriter();
        var record = new BootstrapRecoveryAuditRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            RecoveryAction = BootstrapRecoveryAction.RebindCredentialSource,
            Outcome = "Authorized",
            Details = "Test"
        };

        var act = () => stub.WriteRecoveryAsync(record);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StubBootstrapRecoveryService_when_authorizer_denies_writes_audit_and_returns_failure()
    {
        var authorizer = new Mock<IBootstrapRecoveryAuthorizer>();
        authorizer
            .Setup(x => x.AuthorizeAsync(It.IsAny<BootstrapRecoveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapRecoveryAuthorizationResult { Authorized = false, Reason = "Recovery not configured." });

        var auditWriter = new Mock<IBootstrapAuditWriter>();
        var service = new StubBootstrapRecoveryService(authorizer.Object, auditWriter.Object);
        var request = new BootstrapRecoveryRequest { ActionType = BootstrapRecoveryAction.RestorePlatformAccess, Reason = "Test" };

        var result = await service.ExecuteRecoveryAsync(request);

        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
        auditWriter.Verify(
            x => x.WriteRecoveryAsync(It.Is<BootstrapRecoveryAuditRecord>(r => r.Outcome == "Denied"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StubBootstrapRecoveryService_when_authorizer_authorizes_writes_audit_and_returns_success()
    {
        var authorizer = new Mock<IBootstrapRecoveryAuthorizer>();
        authorizer
            .Setup(x => x.AuthorizeAsync(It.IsAny<BootstrapRecoveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapRecoveryAuthorizationResult { Authorized = true });

        var auditWriter = new Mock<IBootstrapAuditWriter>();
        var service = new StubBootstrapRecoveryService(authorizer.Object, auditWriter.Object);
        var request = new BootstrapRecoveryRequest { ActionType = BootstrapRecoveryAction.RebindCredentialSource, Reason = "Test" };

        var result = await service.ExecuteRecoveryAsync(request);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Recovery unlock completed");
        auditWriter.Verify(
            x => x.WriteRecoveryAsync(It.Is<BootstrapRecoveryAuditRecord>(r => r.Outcome == "Authorized"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
