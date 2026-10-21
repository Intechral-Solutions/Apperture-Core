using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP3: adding a registration is attributed to its own effect on the active registration, never to the rest of the system.
/// </summary>
public sealed class RegistrationRaiseOnlyAssertionTests
{
    private static EntraInstanceRegistration Active(string id = "active", string tenant = ViabilityTestData.Tenant, string? secretRef = null) => new()
    {
        Id = id,
        Enabled = true,
        IsActive = true,
        Authority = "https://login.microsoftonline.com/" + tenant,
        ClientId = "client",
        AzureTenantId = tenant,
        UsesDeploymentSecret = true,
        ClientSecretRef = secretRef,
    };

    [Fact]
    public void An_unchanged_active_registration_passes()
    {
        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(Active(), Active(), "new");

        act.Should().NotThrow();
    }

    [Fact]
    public void No_active_registration_before_and_after_passes()
    {
        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(null, null, "new");

        act.Should().NotThrow();
    }

    [Fact]
    public void Database_managed_columns_are_not_sign_in_settings()
    {
        var before = Active();
        var after = new EntraInstanceRegistration
        {
            Id = "active",
            Enabled = true,
            IsActive = true,
            Authority = "https://login.microsoftonline.com/" + ViabilityTestData.Tenant,
            ClientId = "client",
            AzureTenantId = ViabilityTestData.Tenant,
            UsesDeploymentSecret = true,
            RowVersion = [1, 2, 3],
            UpdatedUtc = DateTimeOffset.UtcNow,
            LastOpenIdValidationSummary = "validated meanwhile",
        };

        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(before, after, "new");

        act.Should().NotThrow("a probe or a timestamp is not the writer's effect on the sign-in");
    }

    [Fact]
    public void The_new_registration_becoming_active_is_a_defect()
    {
        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(null, Active("new"), "new");

        act.Should().Throw<AuditInvariantException>().Which.Message.Should().Contain("active one");
    }

    [Theory]
    [InlineData("other-id", ViabilityTestData.Tenant, null)]
    [InlineData("active", ViabilityTestData.OtherTenant, null)]
    [InlineData("active", ViabilityTestData.Tenant, "a-new-secret-ref")]
    public void Any_change_to_the_active_registration_is_a_defect(string id, string tenant, string? secretRef)
    {
        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(Active(), Active(id, tenant, secretRef), "new");

        act.Should().Throw<AuditInvariantException>().Which.Message.Should().Contain("changed the active registration");
    }

    [Fact]
    public void An_active_registration_appearing_or_disappearing_is_a_defect()
    {
        var appeared = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(null, Active(), "new");
        var vanished = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(Active(), null, "new");

        appeared.Should().Throw<AuditInvariantException>();
        vanished.Should().Throw<AuditInvariantException>();
    }

    [Fact]
    public void The_failure_never_names_a_secret_reference()
    {
        var act = () => RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(Active(secretRef: "secret-ref-1"), Active(secretRef: "secret-ref-2"), "new");

        act.Should().Throw<AuditInvariantException>().Which.Message.Should().NotContain("secret-ref");
    }
}
