using Apperture.ControlPlane.Security.AdministratorViability;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>IAM-10.2b WP3: the typed refusal of a registration change names what is violated and what to do, and nothing internal.</summary>
public sealed class ViabilityRefusalRegistrationTests
{
    [Theory]
    [InlineData(ViabilityVerdictKind.LastViableAdministrator, "last_viable_sign_in_method")]
    [InlineData(ViabilityVerdictKind.EntraIndependentFallbackRequired, "entra_independent_fallback_required")]
    [InlineData(ViabilityVerdictKind.ConfigurationUnavailable, "administrator_viability_unavailable")]
    public void A_registration_refusal_has_the_stable_code_of_what_is_violated_and_says_what_to_do(ViabilityVerdictKind kind, string code)
    {
        var refusal = ViabilityRefusal.For(new ViabilityVerdict(kind, ViabilityView.Restart, "detail"), ViabilityMutation.RegistrationChange);

        refusal.Code.Should().Be(code);
        refusal.Message.Should().NotBeNullOrWhiteSpace();
        if (kind == ViabilityVerdictKind.LastViableAdministrator)
            refusal.Message.Should().Contain("Microsoft registration").And.Contain("local sign-in");
    }

    [Fact]
    public void A_refusal_message_carries_no_development_labels()
    {
        foreach (var kind in new[] { ViabilityVerdictKind.LastViableAdministrator, ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityVerdictKind.ConfigurationUnavailable })
        {
            var message = ViabilityRefusal.For(new ViabilityVerdict(kind, null, "detail"), ViabilityMutation.RegistrationChange).Message;
            message.Should().NotContainAny("IAM-", "WP3", "R-1", "A10.2b", "I-VIABLE", "I-FALLBACK");
        }
    }
}
