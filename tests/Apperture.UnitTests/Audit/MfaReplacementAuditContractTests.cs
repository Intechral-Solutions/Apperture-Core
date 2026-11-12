using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// The audit contracts the authenticator replacement and its refusals will use, declared before any service writes them: what each
/// event may carry, which values are closed, and that no secret, code, candidate or configuration value can enter either. Also pins
/// that the candidate repository is still dormant, so the service that first calls it deliberately updates this pin.
/// </summary>
public sealed class MfaReplacementAuditContractTests
{
    private static readonly UserId User = new("usr-1");

    [Fact]
    public void The_replacement_is_a_required_change_of_the_user_and_carries_only_the_user_and_the_evidence_class()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.MfaReplaced);

        definition.Action.Should().Be("auth.mfa.replaced");
        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.Category.Should().Be(AuditCategories.Identity);
        definition.Severity.Should().Be(AuditSeverities.Elevated);
        definition.TargetType.Should().Be(AuditTargetTypes.User);
        definition.Retired.Should().BeFalse();

        var evt = AuditEvents.MfaReplaced(User, ConfigurationEvidence.Attested);
        evt.Action.Should().Be("auth.mfa.replaced");
        evt.Target!.Id.Should().Be(User.Value);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Fields.Keys.Should().BeEquivalentTo("userId", "configurationEvidence");
        definition.Details!.Validate(definition.Action, evt.Fields);

        var smuggled = new Dictionary<string, object?>(evt.Fields) { ["candidateId"] = "c1" };
        var reject = () => definition.Details.Validate(definition.Action, smuggled);
        reject.Should().Throw<ArgumentException>("a candidate id, a secret or a code has no field to travel in");
    }

    [Fact]
    public void The_viability_refusal_is_an_attempt_against_the_affected_user_that_always_names_the_evidence_class()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AdministratorViabilityBlocked);

        definition.Action.Should().Be("emergency.auth.blocked.administrator_viability");
        definition.WriteClass.Should().Be(AuditWriteClass.Attempt);
        definition.Basis.Should().Be(AuditBases.Attempt);
        definition.Category.Should().Be(AuditCategories.Identity);
        definition.TargetType.Should().Be(AuditTargetTypes.User);
        definition.Defects.Should().BeNullOrEmpty("the refusal records the person who attempted it as the actor and the affected account as the target");

        foreach (var evidence in Enum.GetValues<ConfigurationEvidence>())
        {
            var evt = AuditEvents.AdministratorViabilityBlocked(User, "mfa.replace", "administrator_viability_unavailable", evidence);

            evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
            evt.Target!.Id.Should().Be(User.Value);
            evt.Fields["configurationEvidence"].Should().Be(evidence.ToAuditValue());
            evt.Fields.Keys.Should().BeEquivalentTo("userId", "operation", "code", "configurationEvidence");
            definition.Details!.Validate(definition.Action, evt.Fields);
        }
    }

    [Theory]
    [InlineData("recovery_access.designate")]
    [InlineData("recovery_access.revoke")]
    [InlineData("mfa.reset")]
    [InlineData("mfa.replace")]
    public void Every_guarded_command_line_operation_can_be_recorded_in_a_refusal(string operation)
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AdministratorViabilityBlocked);
        var evt = AuditEvents.AdministratorViabilityBlocked(User, operation, "entra_independent_fallback_required", ConfigurationEvidence.NotRequired);

        var act = () => definition.Details!.Validate(definition.Action, evt.Fields);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_refusal_of_an_unknown_operation_or_with_a_value_of_the_configuration_is_rejected_by_the_schema()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AdministratorViabilityBlocked);
        var good = AuditEvents.AdministratorViabilityBlocked(User, "mfa.replace", "administrator_viability_unavailable", ConfigurationEvidence.Unavailable).Fields;

        var unknownOperation = new Dictionary<string, object?>(good) { ["operation"] = "database.repair" };
        var untrustedEvidence = new Dictionary<string, object?>(good) { ["configurationEvidence"] = "trusted" };
        var withTenant = new Dictionary<string, object?>(good) { ["tenantId"] = "tenant-1" };
        var withoutEvidence = good.Where(kv => kv.Key != "configurationEvidence").ToDictionary(kv => kv.Key, kv => kv.Value);

        new[] { unknownOperation, untrustedEvidence, withTenant }
            .Select(fields => (Action)(() => definition.Details!.Validate(definition.Action, fields)))
            .Should().AllSatisfy(act => act.Should().Throw<ArgumentException>());
        var missing = () => definition.Details!.Validate(definition.Action, withoutEvidence);
        missing.Should().Throw<ArgumentException>("a refusal always names the evidence class it was made under").WithMessage("*requires field 'configurationEvidence'*");
    }

    [Fact]
    public void The_viability_refusal_producer_cannot_be_built_without_an_evidence_class()
    {
        // The producer's parameter is not nullable: a refusal made under no declared evidence class cannot be written.
        var parameter = typeof(AuditEvents).GetMethod(nameof(AuditEvents.AdministratorViabilityBlocked))!.GetParameters().Single(p => p.Name == "configurationEvidence");

        parameter.ParameterType.Should().Be(typeof(ConfigurationEvidence));
        parameter.HasDefaultValue.Should().BeFalse();
    }

    /// <summary>
    /// The files that name the candidate repository: its contract, its implementation, the registrations, and the one service that
    /// replaces an authenticator (whose writes the writer-coverage guard counts by the receiver's type). Any other production file that
    /// names it must be added here in the same change, with its writes classified.
    /// </summary>
    private static readonly string[] FilesNamingTheCandidateRepository =
    [
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/NativeMfaService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs",
        "Apperture.Core/Apperture.Core.Application/Services/INativeMfaReplacementCandidateRepository.cs",
        "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNativeMfaReplacementCandidateRepository.cs",
    ];

    [Fact]
    public void The_candidate_repository_is_named_by_its_contract_its_implementation_and_the_registrations_only()
    {
        var naming = RepositorySource.Production
            .Where(f => f.Mentions("INativeMfaReplacementCandidateRepository") || f.Mentions("SqlNativeMfaReplacementCandidateRepository"))
            .Select(f => f.RelativePath)
            .ToList();

        naming.Should().BeEquivalentTo(FilesNamingTheCandidateRepository);
    }

    [Fact]
    public void The_viability_refusal_is_built_only_by_the_one_mapping_from_a_decision()
    {
        // Every refused Recovery Access or authenticator operation records its row through one function, so the operation, the code and
        // the evidence class come from the decision and cannot drift apart between the services that write it.
        var callers = RepositorySource.Production
            .Where(f => f.Code.Contains("AuditEvents.AdministratorViabilityBlocked(", StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();

        callers.Should().BeEquivalentTo(["Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/ViabilityRefusalAudit.cs"]);
    }

    [Fact]
    public void The_replacement_is_recorded_only_by_the_switch_of_the_native_mfa_service()
    {
        var callers = RepositorySource.Production
            .Where(f => f.Code.Contains("AuditEvents.MfaReplaced(", StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();

        callers.Should().BeEquivalentTo(["Apperture.ControlPlane/Apperture.ControlPlane.Security/NativeMfaService.cs"],
            "the row commits with the switch, in its audited unit");
    }
}
