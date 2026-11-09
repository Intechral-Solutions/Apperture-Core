using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The one mapping from a viability decision to the refusal row of a guarded Recovery Access or authenticator operation (U7, S-5): every
/// mutation and every evidence value, including the decision without an evidence class, which the refusal's schema cannot leave out.
/// </summary>
public sealed class ViabilityRefusalAuditTests
{
    private static readonly UserId User = new("stored-id");

    private static readonly ViabilityVerdict Refused = new(ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityView.Now, "x");

    public static IEnumerable<object[]> GuardedMutations() =>
    [
        [ViabilityMutation.RecoveryAccessDesignation, "recovery_access.designate"],
        [ViabilityMutation.RecoveryAccessRevocation, "recovery_access.revoke"],
        [ViabilityMutation.AuthenticatorReset, "mfa.reset"],
        [ViabilityMutation.AuthenticatorReplacement, "mfa.replace"],
    ];

    public static IEnumerable<object?[]> Evidence() =>
    [
        [ConfigurationEvidence.Attested, ConfigurationEvidenceNames.Attested],
        [ConfigurationEvidence.NotRequired, ConfigurationEvidenceNames.NotRequired],
        [ConfigurationEvidence.Unavailable, ConfigurationEvidenceNames.Unavailable],
        [null, ConfigurationEvidenceNames.NotRequired],
    ];

    public static IEnumerable<object?[]> EveryGuardedMutationAndEvidence() =>
        from m in GuardedMutations()
        from e in Evidence()
        select new object?[] { m[0], m[1], e[0], e[1] };

    [Theory]
    [MemberData(nameof(EveryGuardedMutationAndEvidence))]
    public void A_refusal_names_the_operation_of_its_mutation_the_code_of_its_refusal_and_the_evidence_its_decision_used(
        ViabilityMutation mutation, string operation, ConfigurationEvidence? evidence, string recorded)
    {
        var decision = new ViabilityDecision(mutation, Refused, evidence, ViabilityDecisionBasis.LocalSignIn);

        var row = ViabilityRefusalAudit.Blocked(User, decision);

        row.Action.Should().Be(AuditActionNames.AdministratorViabilityBlocked);
        row.Target!.Id.Should().Be("stored-id");
        row.Fields["userId"].Should().Be("stored-id");
        row.Fields["operation"].Should().Be(operation);
        row.Fields["code"].Should().Be(decision.Refusal().Code);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(recorded,
            "a decision's own class is recorded; one made by a host on its own options (no class) needed no evidence about the installation");
    }

    [Theory]
    [MemberData(nameof(EveryGuardedMutationAndEvidence))]
    public void An_explicit_code_is_recorded_for_a_decision_whatever_its_verdict(
        ViabilityMutation mutation, string operation, ConfigurationEvidence? evidence, string recorded)
    {
        var allowed = new ViabilityDecision(mutation, ViabilityVerdict.Allowed, evidence, ViabilityDecisionBasis.FullEvaluation);

        var row = ViabilityRefusalAudit.Blocked(User, allowed, "administrator_access_changed");

        row.Fields["operation"].Should().Be(operation);
        row.Fields["code"].Should().Be("administrator_access_changed");
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(recorded);
    }

    [Theory]
    [InlineData(ViabilityMutation.AdministratorRoleRemoval)]
    [InlineData(ViabilityMutation.LocalCredentialDisable)]
    [InlineData(ViabilityMutation.IdentityLinkRemoval)]
    [InlineData(ViabilityMutation.RegistrationChange)]
    public void A_change_that_is_not_a_guarded_operation_has_no_refusal_row_here(ViabilityMutation mutation)
    {
        var decision = new ViabilityDecision(mutation, Refused, ConfigurationEvidence.Attested, ViabilityDecisionBasis.FullEvaluation);

        var act = () => ViabilityRefusalAudit.Blocked(User, decision);

        act.Should().Throw<ArgumentOutOfRangeException>("its refusal is recorded by an event of its own and must not borrow a name");
    }

    [Fact]
    public void Every_mutation_is_either_mapped_or_rejected()
    {
        foreach (var mutation in Enum.GetValues<ViabilityMutation>())
        {
            var guarded = GuardedMutations().Any(m => (ViabilityMutation)m[0] == mutation);
            var act = () => ViabilityRefusalAudit.OperationOf(mutation);
            if (guarded)
                act.Should().NotThrow();
            else
                act.Should().Throw<ArgumentOutOfRangeException>();
        }

        ViabilityRefusalAudit.OperationOf(ViabilityMutation.AuthenticatorReplacement).Should().Be("mfa.replace");
    }

    [Fact]
    public void A_decision_that_allows_has_no_refusal_code_of_its_own()
    {
        var allowed = new ViabilityDecision(ViabilityMutation.AuthenticatorReset, ViabilityVerdict.Allowed, ConfigurationEvidence.NotRequired, ViabilityDecisionBasis.PostureOnly);

        var act = () => ViabilityRefusalAudit.Blocked(User, allowed);

        act.Should().Throw<ArgumentException>("an allowed verdict has no refusal; a rule of the operation's own passes its code explicitly");
    }

    [Fact]
    public void The_evidence_a_success_row_leaves_out_is_recorded_as_not_required_only_on_a_refusal()
    {
        ViabilityRefusalAudit.RefusalEvidence(null).Should().Be(ConfigurationEvidence.NotRequired);
        foreach (var evidence in Enum.GetValues<ConfigurationEvidence>())
            ViabilityRefusalAudit.RefusalEvidence(evidence).Should().Be(evidence, "a decision's own class is never replaced");

        // The success producers keep their optional detail: a decision without a class leaves it out.
        AuditEvents.MfaReplaced(User, null).Fields.Should().NotContainKey(ConfigurationEvidenceNames.AuditDetailName);
        AuditEvents.MfaReset(User, null).Fields.Should().NotContainKey(ConfigurationEvidenceNames.AuditDetailName);
        AuditEvents.RecoveryAccessRevoked(User, null).Fields.Should().NotContainKey(ConfigurationEvidenceNames.AuditDetailName);
        AuditEvents.RecoveryAccessDesignated(User, null, null).Fields.Should().NotContainKey(ConfigurationEvidenceNames.AuditDetailName);
    }
}
