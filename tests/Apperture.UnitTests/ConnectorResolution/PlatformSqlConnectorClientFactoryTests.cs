using Apperture.ConnectorResolution;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Apperture.UnitTests.ConnectorResolution;

public sealed class PlatformSqlConnectorClientFactoryTests
{
    private static readonly Connection DummyConnection = new(
        new ConnectionId("test-conn"),
        WellKnownConnectorDefinitionIds.PlatformSql,
        "test",
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        null,
        null);

    [Fact]
    public void Create_OmitsTrustServerCertificate_UsesTrustServerCertificateTrue()
    {
        var factory = new PlatformSqlConnectorClientFactory();
        var material = new ResolvedConnectionMaterial(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Server"] = "localhost",
                ["Database"] = "db",
                ["UserId"] = "sa",
                ["Password"] = "x",
            });

        var sql = (SqlConnection)factory.Create(DummyConnection, material);
        var parsed = new SqlConnectionStringBuilder(sql.ConnectionString);
        parsed.TrustServerCertificate.Should().BeTrue();
    }

    [Fact]
    public void Create_TrustServerCertificateFalse_PreservesFalse()
    {
        var factory = new PlatformSqlConnectorClientFactory();
        var material = new ResolvedConnectionMaterial(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Server"] = "localhost",
                ["Database"] = "db",
                ["UserId"] = "sa",
                ["Password"] = "x",
                ["TrustServerCertificate"] = "false",
            });

        var sql = (SqlConnection)factory.Create(DummyConnection, material);
        var parsed = new SqlConnectionStringBuilder(sql.ConnectionString);
        parsed.TrustServerCertificate.Should().BeFalse();
    }

    [Fact]
    public void Create_EncryptFalse_SetsOptionalEncrypt()
    {
        var factory = new PlatformSqlConnectorClientFactory();
        var material = new ResolvedConnectionMaterial(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Server"] = "localhost",
                ["Database"] = "db",
                ["UserId"] = "sa",
                ["Password"] = "x",
                ["Encrypt"] = "false",
            });

        var sql = (SqlConnection)factory.Create(DummyConnection, material);
        var parsed = new SqlConnectionStringBuilder(sql.ConnectionString);
        parsed.Encrypt.Should().Be(SqlConnectionEncryptOption.Optional);
    }
}
