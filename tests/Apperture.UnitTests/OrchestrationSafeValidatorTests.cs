using Apperture.Core.Application;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests;

public sealed class OrchestrationSafeValidatorTests
{
    [Fact]
    public void ValidateParameters_WhenNull_DoesNotThrow()
    {
        Action act = () => OrchestrationSafeValidator.ValidateParameters(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateParameters_WhenEmpty_DoesNotThrow()
    {
        Action act = () => OrchestrationSafeValidator.ValidateParameters(new Dictionary<string, object?>());
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateParameters_WithSafeTypes_DoesNotThrow()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["a"] = "value",
            ["b"] = 42,
            ["c"] = true,
            ["d"] = Guid.NewGuid(),
            ["e"] = new SecretRef("kv:my-secret", null)
        };
        Action act = () => OrchestrationSafeValidator.ValidateParameters(parameters);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateParameters_WithResolvedSecret_ThrowsArgumentException()
    {
        var resolved = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret"),
            Version: null);
        var parameters = new Dictionary<string, object?> { ["key"] = resolved };

        Action act = () => OrchestrationSafeValidator.ValidateParameters(parameters);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("parameters")
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public void ValidateParameters_WithSecretMaterial_ThrowsArgumentException()
    {
        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret");
        var parameters = new Dictionary<string, object?> { ["key"] = material };

        Action act = () => OrchestrationSafeValidator.ValidateParameters(parameters);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("parameters")
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public void ValidateSignalPayload_WhenNull_DoesNotThrow()
    {
        Action act = () => OrchestrationSafeValidator.ValidateSignalPayload(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSignalPayload_WithSafeType_DoesNotThrow()
    {
        Action act = () => OrchestrationSafeValidator.ValidateSignalPayload("payload");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSignalPayload_WithResolvedSecret_ThrowsArgumentException()
    {
        var resolved = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "s"),
            Version: null);

        Action act = () => OrchestrationSafeValidator.ValidateSignalPayload(resolved);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("payload")
            .WithMessage("*secret-bearing*");
    }

    [Fact]
    public void ValidateSignalPayload_WithSecretMaterial_ThrowsArgumentException()
    {
        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "s");

        Action act = () => OrchestrationSafeValidator.ValidateSignalPayload(material);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("payload")
            .WithMessage("*secret-bearing*");
    }
}
