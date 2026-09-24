using Apperture.ConnectorResolution;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// LC5: when a Connection's secrets cannot be resolved, verification reports the failure as a fact but never echoes the
/// underlying exception text, which can carry secret identifiers, provider paths or key names. The summary is persisted
/// on the Connection and shown to anyone who can read it, so it must be a fixed, safe message.
/// </summary>
public sealed class ConnectionVerifierSecretFailureTests
{
    private const string LeakyMessage = "Secret 3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11 (vault://prod/kv/sql-password) not found for key Password";

    private static Connection ConnectionFor(ConnectorDefinitionId connector) =>
        new(
            new ConnectionId("c-1"), connector, "Warehouse",
            new Dictionary<string, string> { ["Host"] = "h", ["Username"] = "u", ["BaseUrl"] = "https://x.example" },
            new Dictionary<string, string> { ["Password"] = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11", ["ApiKey"] = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11" },
            null, null);

    private static ISecretResolver ThrowingResolver()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(LeakyMessage));
        return resolver.Object;
    }

    private static void AssertSafe(ConnectionVerificationResult result)
    {
        result.Status.Should().Be(ConnectionVerificationStatus.Failed);
        result.Summary.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
        result.Summary.Should().NotContain("3f2a9c1e").And.NotContain("vault://").And.NotContain("Password").And.NotContain(LeakyMessage);
    }

    [Fact]
    public async Task Platform_sql_reports_unresolvable_secrets_without_the_exception_text()
    {
        var verifier = new PlatformSqlConnectionVerifier(ThrowingResolver(), new Mock<IConnectorClientFactory<System.Data.IDbConnection>>().Object);

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformSql)));
    }

    [Fact]
    public async Task Sftp_reports_unresolvable_secrets_without_the_exception_text()
    {
        var verifier = new SftpConnectionVerifier(ThrowingResolver(), new SftpConnectorClientFactory());

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformSftp)));
    }

    [Fact]
    public async Task Campus_nexus_reports_unresolvable_secrets_without_the_exception_text()
    {
        var verifier = new CampusNexusConnectionVerifier(ThrowingResolver(), new CampusNexusApiConnectorClientFactory());

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformCampusNexus)));
    }

    [Fact]
    public void The_fixed_message_is_operator_meaningful_and_names_no_technical_detail()
    {
        ConnectionVerificationMessages.SecretResolutionFailed.Should().Contain("secret");
    }
}
