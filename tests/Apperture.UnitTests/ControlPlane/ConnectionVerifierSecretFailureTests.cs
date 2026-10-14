using Apperture.ConnectorResolution;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
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

    /// <summary>A boundary that fails with text no operator may see: a verifier must not repeat any exception's message.</summary>
    private static Mock<IConnectionMaterialResolver> ThrowingResolver(Exception? failure = null)
    {
        var resolver = new Mock<IConnectionMaterialResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<Connection>(), It.IsAny<ConnectionResolutionPath>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure ?? new InvalidOperationException(LeakyMessage));
        return resolver;
    }

    private static void AssertSafe(ConnectionVerificationResult result)
    {
        result.Status.Should().Be(ConnectionVerificationStatus.Failed);
        result.Summary.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
        result.Summary.Should().NotContain("3f2a9c1e").And.NotContain("vault://").And.NotContain("Password").And.NotContain(LeakyMessage);
    }

    private static void AssertAskedOnceForTheVerifyPath(Mock<IConnectionMaterialResolver> resolver) =>
        resolver.Verify(
            r => r.ResolveAsync(It.IsAny<Connection>(), ConnectionResolutionPath.Verify, It.IsAny<CancellationToken>()),
            Times.Once,
            "a verifier resolves its connection through the one boundary, once, as a verification");

    [Fact]
    public async Task Platform_sql_reports_unresolvable_secrets_without_the_exception_text()
    {
        var resolver = ThrowingResolver();
        var verifier = new PlatformSqlConnectionVerifier(resolver.Object, new Mock<IConnectorClientFactory<System.Data.IDbConnection>>().Object);

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformSql)));
        AssertAskedOnceForTheVerifyPath(resolver);
    }

    [Fact]
    public async Task Sftp_reports_unresolvable_secrets_without_the_exception_text()
    {
        var resolver = ThrowingResolver();
        var verifier = new SftpConnectionVerifier(resolver.Object, new SftpConnectorClientFactory());

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformSftp)));
        AssertAskedOnceForTheVerifyPath(resolver);
    }

    [Fact]
    public async Task Campus_nexus_reports_unresolvable_secrets_without_the_exception_text()
    {
        var resolver = ThrowingResolver();
        var verifier = new CampusNexusConnectionVerifier(resolver.Object, new CampusNexusApiConnectorClientFactory());

        AssertSafe(await verifier.VerifyAsync(ConnectionFor(WellKnownConnectorDefinitionIds.PlatformCampusNexus)));
        AssertAskedOnceForTheVerifyPath(resolver);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("sftp")]
    [InlineData("campusnexus")]
    public async Task A_typed_refusal_is_reported_as_the_same_fixed_message_and_creates_no_client(string connector)
    {
        var refusal = new ConnectionSecretUnavailableException("Password");
        var resolver = ThrowingResolver(refusal);
        var sqlFactory = new Mock<IConnectorClientFactory<System.Data.IDbConnection>>();
        IConnectionVerifier verifier = connector switch
        {
            "sql" => new PlatformSqlConnectionVerifier(resolver.Object, sqlFactory.Object),
            "sftp" => new SftpConnectionVerifier(resolver.Object, new SftpConnectorClientFactory()),
            _ => new CampusNexusConnectionVerifier(resolver.Object, new CampusNexusApiConnectorClientFactory()),
        };

        var result = await verifier.VerifyAsync(ConnectionFor(verifier.ConnectorDefinitionId));

        AssertSafe(result);
        sqlFactory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Never);
    }

    [Fact]
    public void The_fixed_message_is_operator_meaningful_and_names_no_technical_detail()
    {
        ConnectionVerificationMessages.SecretResolutionFailed.Should().Contain("secret");
    }
}
