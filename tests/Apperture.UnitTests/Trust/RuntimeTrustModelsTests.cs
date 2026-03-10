using Apperture.Core.Security.Trust;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Trust;

public sealed class RuntimeTrustModelsTests
{
    [Fact]
    public void RuntimeIdentityRegistration_Can_Be_Created()
    {
        var registration = new RuntimeIdentityRegistration(
            "runtime-1",
            "PublicKey",
            "pk-abc",
            null);

        registration.RuntimeName.Should().Be("runtime-1");
        registration.CredentialType.Should().Be("PublicKey");
        registration.PublicKeyOrThumbprint.Should().Be("pk-abc");
        registration.ManagedIdentityPrincipalId.Should().BeNull();
    }

    [Fact]
    public void RuntimeTrustValidationResult_Has_Default_Claims()
    {
        var result = new RuntimeTrustValidationResult(true);

        result.IsValid.Should().BeTrue();
        result.Claims.Should().BeNull();
    }

    [Fact]
    public void EnrollmentTokenValidationResult_Carries_FailureReason()
    {
        var result = new EnrollmentTokenValidationResult(false, null, "Invalid token");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("Invalid token");
    }
}

