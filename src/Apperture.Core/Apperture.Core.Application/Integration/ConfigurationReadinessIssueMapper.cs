using Apperture.Core.Domain.Connector;
using Apperture.ModuleSdk.Integration.Readiness;

namespace Apperture.Core.Application.Integration;

/// <summary>Maps SDK configuration readiness reasons onto control-plane readiness issues.</summary>
public static class ConfigurationReadinessIssueMapper
{
    public static IReadOnlyList<IntegrationReadinessIssue> ToIntegrationIssues(
        IEnumerable<ConfigurationReadinessReason> reasons,
        IReadOnlyList<Binding> bindings)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(bindings);

        var bindingBySlot = bindings.ToDictionary(b => b.SlotName, StringComparer.Ordinal);
        return reasons.Select(r => ToIntegrationIssue(r, bindingBySlot)).ToList();
    }

    private static IntegrationReadinessIssue ToIntegrationIssue(
        ConfigurationReadinessReason reason,
        IReadOnlyDictionary<string, Binding> bindingBySlot)
    {
        var code = MapCode(reason.Code);
        var severity = reason.Severity == ConfigurationReadinessSeverity.Advisory
            ? IntegrationReadinessIssueSeverity.Advisory.ToString()
            : IntegrationReadinessIssueSeverity.Blocking.ToString();

        string? bindingId = null;
        string? connectionId = null;
        if (!string.IsNullOrWhiteSpace(reason.ConnectionKey)
            && bindingBySlot.TryGetValue(reason.ConnectionKey, out var binding))
        {
            bindingId = binding.Id.Value;
            connectionId = binding.ConnectionId?.Value;
        }

        return new IntegrationReadinessIssue(
            code,
            reason.Message,
            reason.ConnectionKey ?? reason.SettingKey,
            bindingId,
            connectionId,
            severity);
    }

    private static string MapCode(string sdkCode) =>
        sdkCode switch
        {
            ConfigurationReadinessReasonCodes.IntegrationDisabled => IntegrationReadinessIssueCodes.IntegrationNotActive,
            ConfigurationReadinessReasonCodes.MissingConnectionBinding => IntegrationReadinessIssueCodes.MissingSlotBinding,
            ConfigurationReadinessReasonCodes.ConnectionDisabled => IntegrationReadinessIssueCodes.ConnectionInactive,
            ConfigurationReadinessReasonCodes.ConnectionUnavailable => IntegrationReadinessIssueCodes.ConnectionMissing,
            ConfigurationReadinessReasonCodes.IncompatibleConnectionType => IntegrationReadinessIssueCodes.BindingNotEligible,
            ConfigurationReadinessReasonCodes.VerificationFailed => IntegrationReadinessIssueCodes.ConnectionVerificationFailed,
            ConfigurationReadinessReasonCodes.VerificationUnverified => IntegrationReadinessIssueCodes.ConnectionVerificationUnverified,
            ConfigurationReadinessReasonCodes.VerificationUnsupported => IntegrationReadinessIssueCodes.ConnectionVerificationUnavailable,
            ConfigurationReadinessReasonCodes.VerificationStale => IntegrationReadinessIssueCodes.ConnectionVerificationUnverified,
            _ => sdkCode
        };
}
