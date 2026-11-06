using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// Ensures no method returns or logs plaintext secret material; SecretMaterial and ResolvedSecret ToString are redacted.
/// </summary>
public sealed class SecretManagementSafeHandlingTests
{
    [Fact]
    public void SecretMaterial_ToString_does_not_expose_sensitive_values()
    {
        var material = new SecretMaterial(
            SecretMaterialType.OpaqueString,
            OpaqueString: "MySecretPassword123!");
        var s = material.ToString();

        s.Should().NotContain("MySecretPassword123!");
        s.ToUpperInvariant().Should().NotContain("PASSWORD");
        s.Should().Contain("SecretMaterial");
    }

    [Theory]
    [InlineData(SecretMaterialType.Json, "{\"key\":\"secret\"}")]
    [InlineData(SecretMaterialType.Bytes)]
    public void SecretMaterial_ToString_does_not_expose_any_material_content(SecretMaterialType type, string? json = null)
    {
        var material = type switch
        {
            SecretMaterialType.OpaqueString => new SecretMaterial(type, OpaqueString: "opaque"),
            SecretMaterialType.Json => new SecretMaterial(type, Json: json ?? "{}"),
            SecretMaterialType.Bytes => new SecretMaterial(type, Bytes: [1, 2, 3]),
            SecretMaterialType.X509Certificate => new SecretMaterial(type, X509Certificate: [4, 5, 6]),
            _ => new SecretMaterial(type)
        };

        var s = material.ToString();
        s.Should().NotContain("opaque");
        s.Should().NotContain("secret");
        s.Should().NotContain("1");
    }

    [Fact]
    public void ResolvedSecret_ToString_does_not_expose_material_content()
    {
        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "api-key-12345");
        var resolved = new ResolvedSecret(new SecretRef("guid-here"), material, Version: "v1");
        var s = resolved.ToString();

        s.Should().NotContain("api-key-12345");
        s.Should().Contain("ResolvedSecret");
        s.Should().Contain("OpaqueString");
    }

    [Fact]
    public async Task CompositeSecretResolver_throws_without_leaking_ref_when_no_resolver()
    {
        const string refValue = "kv:my-secret-name";
        const string refVersion = "v-sentinel-7";
        var registry = new Mock<ISecretResolverRegistry>();
        registry.Setup(x => x.GetResolverFor(It.IsAny<SecretRef>())).Returns((ISecretResolver?)null);
        var resolver = new CompositeSecretResolver(registry.Object);
        var secretRef = new SecretRef(refValue, refVersion);

        Func<Task> act = () => resolver.ResolveAsync(secretRef).AsTask();

        var thrown = await act.Should().ThrowExactlyAsync<InvalidOperationException>(
            "an unresolvable reference is refused with the generic no-resolver failure");

        thrown.Which.Message.Should().Contain("No resolver available");
        thrown.Which.Message.Should().NotContain(refValue, "the exception message must not carry the secret reference");
        thrown.Which.Message.Should().NotContain(refVersion);
        // ToString() adds the type, any inner exception and the stack trace: nothing there may carry the reference either.
        thrown.Which.ToString().Should().NotContain(refValue).And.NotContain(refVersion);
        registry.Verify(x => x.GetResolverFor(secretRef), Times.Once, "the refusal must come from the registry lookup, not from elsewhere");
    }

    [Fact]
    public void SecretDescriptor_does_not_contain_secret_content_properties()
    {
        var props = typeof(SecretDescriptor).GetProperties();
        var sensitiveNames = new[] { "OpaqueString", "Json", "Bytes", "X509Certificate", "Payload", "ProtectedPayload" };
        var hasSensitive = props.Any(p => sensitiveNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

        hasSensitive.Should().BeFalse("SecretDescriptor must not expose secret content (OpaqueString, Json, Bytes, etc.)");
    }

    [Fact]
    public void SecretAuditRecord_does_not_contain_secret_content_properties()
    {
        var props = typeof(SecretAuditRecord).GetProperties();
        var sensitiveNames = new[] { "OpaqueString", "Json", "Bytes", "X509Certificate", "Payload", "ProtectedPayload", "Material" };
        var hasSensitive = props.Any(p => sensitiveNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

        hasSensitive.Should().BeFalse("SecretAuditRecord must not expose secret content");
    }
}
