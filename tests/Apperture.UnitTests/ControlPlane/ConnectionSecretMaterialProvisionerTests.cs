using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ConnectionSecretMaterialProvisionerTests
{
    [Fact]
    public void ValidateConnectionMaterial_PlatformSql_MissingRequired_Throws()
    {
        var def = WellKnownConnectorDefinitions.CreatePlatformSql();
        var act = () => ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(
            def,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Server");
    }

    [Fact]
    public void ValidateConnectionMaterial_PlatformSql_WithServerDatabaseAndPasswordRef_Succeeds()
    {
        var def = WellKnownConnectorDefinitions.CreatePlatformSql();
        var secretId = Guid.NewGuid().ToString("D");
        ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(
            def,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Server"] = "localhost",
                ["Database"] = "Apperture",
            },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = secretId });
    }

    [Fact]
    public void ValidateConnectionMaterial_RejectsPlaintextSecretRef()
    {
        var def = WellKnownConnectorDefinitions.CreatePlatformSql();
        var act = () => ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(
            def,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Server"] = "localhost",
                ["Database"] = "Apperture",
            },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "not-a-guid" });
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("GUID");
    }

    [Fact]
    public void NormalizeToDeclaredFieldKeys_MapsJsonStyleKeysToDeclaredCasing()
    {
        var def = WellKnownConnectorDefinitions.CreatePlatformSql();
        var normalized = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["server"] = "localhost",
                ["database"] = "Apperture",
                ["password"] = Guid.NewGuid().ToString("D"),
            },
            def);

        normalized.Should().ContainKey("Server");
        normalized.Should().ContainKey("Database");
        normalized.Should().ContainKey("Password");
        normalized["Server"].Should().Be("localhost");
    }

    [Fact]
    public void NormalizeToDeclaredFieldKeys_UnknownKey_Throws()
    {
        var def = WellKnownConnectorDefinitions.CreatePlatformSql();
        var act = () => ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(
            new Dictionary<string, string> { ["notAField"] = "x" },
            def);
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("notAField");
    }
}
