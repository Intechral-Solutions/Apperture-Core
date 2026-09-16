using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Readiness;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Shared readiness projection from persisted facts (SDK-6C configuration + legacy slot bindings).
/// Used by control-plane APIs and scheduled dispatch enforcement.
/// </summary>
public sealed class IntegrationDispatchReadinessEvaluator : IIntegrationDispatchReadinessEvaluator
{
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IBindingRepository _bindingRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IModuleSlotRegistry _moduleSlotRegistry;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IBindingEligibilityService _eligibilityService;
    private readonly IConnectionVerificationReadinessPolicy _verificationReadinessPolicy;
    private readonly IIntegrationConfigurationRegistry _configurationRegistry;
    private readonly IntegrationConfigurationEvidenceBuilder _configurationEvidence;
    private readonly TimeProvider _timeProvider;

    public IntegrationDispatchReadinessEvaluator(
        IIntegrationRepository integrationRepository,
        IBindingRepository bindingRepository,
        IScheduleRepository scheduleRepository,
        IModuleSlotRegistry moduleSlotRegistry,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IConnectionVerificationReadinessPolicy verificationReadinessPolicy,
        IIntegrationConfigurationRegistry configurationRegistry,
        IntegrationConfigurationEvidenceBuilder configurationEvidence,
        TimeProvider timeProvider)
    {
        _integrationRepository = integrationRepository;
        _bindingRepository = bindingRepository;
        _scheduleRepository = scheduleRepository;
        _moduleSlotRegistry = moduleSlotRegistry;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _verificationReadinessPolicy = verificationReadinessPolicy;
        _configurationRegistry = configurationRegistry;
        _configurationEvidence = configurationEvidence;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationReadinessProjection?> EvaluateForDispatchAsync(
        IntegrationId integrationId,
        string? procedureId,
        CancellationToken cancellationToken = default)
    {
        var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return await EvaluateAsync(integration, bindings, schedules, procedureId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IntegrationReadinessProjection> EvaluateAsync(
        DomainIntegration integration,
        IReadOnlyList<Binding> bindings,
        IReadOnlyList<ScheduleDefinition> schedules,
        string? procedureId,
        CancellationToken cancellationToken)
    {
        var issues = new List<IntegrationReadinessIssue>();

        // One read per distinct bound Connection, for the whole evaluation. Readiness is evaluated per
        // Integration by the fleet list behind /integrations (design gate R21), so a per-binding repository
        // call inside each branch multiplies across the whole fleet. This lookup replaces the existing
        // per-binding read in EvaluateLegacySlotBindingsAsync as well, so the net query count goes DOWN.
        var boundConnections = new Dictionary<string, Connection?>(StringComparer.Ordinal);
        foreach (var connectionId in bindings
                     .Select(b => b.ConnectionId)
                     .Where(id => id is not null)
                     .Select(id => id!.Value)
                     .Distinct(StringComparer.Ordinal))
        {
            boundConnections[connectionId] = await _connectionRepository
                .GetByIdAsync(new ConnectionId(connectionId), cancellationToken)
                .ConfigureAwait(false);
        }

        if (_configurationRegistry.TryGetModuleConfiguration(integration.ModuleId, out var moduleConfiguration))
        {
            var evidence = await _configurationEvidence
                .BuildAsync(integration, moduleConfiguration, bindings, cancellationToken)
                .ConfigureAwait(false);
            var evaluatedAt = _timeProvider.GetUtcNow();
            IReadOnlyList<ConfigurationReadinessReason> reasons;
            if (!string.IsNullOrWhiteSpace(procedureId))
            {
                reasons = ConfigurationReadinessEvaluator.EvaluateProcedure(
                        procedureId,
                        moduleConfiguration.Settings,
                        moduleConfiguration.Connections,
                        moduleConfiguration.Procedures,
                        evidence,
                        evaluatedAt)
                    .Reasons;
            }
            else
            {
                reasons = ConfigurationReadinessEvaluator.EvaluateIntegration(
                        moduleConfiguration.Settings,
                        moduleConfiguration.Connections,
                        evidence,
                        evaluatedAt)
                    .Reasons;
            }

            issues.AddRange(ConfigurationReadinessIssueMapper.ToIntegrationIssues(reasons, bindings));
        }
        else
        {
            if (integration.State != IntegrationState.Active)
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.IntegrationNotActive,
                    $"Integration is {integration.State}; set the integration to Active before running workflows.",
                    null,
                    null,
                    null));
            }

            await EvaluateLegacySlotBindingsAsync(integration, bindings, boundConnections, issues, cancellationToken).ConfigureAwait(false);
        }

        foreach (var s in schedules)
        {
            if (s.IntegrationId != integration.Id)
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.ScheduleOwnershipMismatch,
                    $"Schedule '{s.Name}' ({s.Id.Value}) is not correctly associated with this integration in persisted data.",
                    null,
                    null,
                    null));
            }
        }

        // Design gate §8.7 "Defense in depth": applies uniformly regardless of which branch above
        // produced the rest of the issues -- cross-context validity is a platform invariant, not a
        // module-configuration concern. Ownership is read from persisted columns, so this fires on
        // invalid data however it got there: old rows, a direct DB edit, or a future regression.
        foreach (var binding in bindings.OrderBy(b => b.Id.Value, StringComparer.Ordinal))
        {
            if (binding.ConnectionId is not { } connectionId)
                continue;

            if (!boundConnections.TryGetValue(connectionId.Value, out var connection) || connection is null)
                continue; // already covered by ConnectionMissing where applicable.

            if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, connection.ResourceContextId))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingCrossContext,
                    $"Binding for slot '{binding.SlotName}' references connection '{connection.Name}' in ResourceContext " +
                    $"'{connection.ResourceContextId.Value}', which this integration (in '{integration.ResourceContextId.Value}') " +
                    $"cannot reach. Remove this binding, or move the integration or the connection so they share a context.",
                    binding.SlotName,
                    binding.Id.Value,
                    connectionId.Value));
            }
        }

        var blockingIssueCount = issues.Count(i =>
            string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal));
        var advisoryIssueCount = issues.Count(i =>
            string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Advisory.ToString(), StringComparison.Ordinal));
        var runnable = integration.State == IntegrationState.Active && blockingIssueCount == 0;
        return new IntegrationReadinessProjection(
            integration.Id.Value,
            integration.ModuleId,
            runnable,
            blockingIssueCount,
            advisoryIssueCount,
            issues);
    }

    private async Task EvaluateLegacySlotBindingsAsync(
        DomainIntegration integration,
        IReadOnlyList<Binding> bindings,
        IReadOnlyDictionary<string, Connection?> boundConnections,
        List<IntegrationReadinessIssue> issues,
        CancellationToken cancellationToken)
    {
        var slots = _moduleSlotRegistry.GetSlotsForModule(integration.ModuleId);
        var activeBySlot = bindings
            .Where(b => b.State == BindingState.Active)
            .ToLookup(b => b.SlotName, StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            var list = activeBySlot[slot.SlotName].ToList();
            if (list.Count == 0)
            {
                var nonActive = bindings.FirstOrDefault(b =>
                    string.Equals(b.SlotName, slot.SlotName, StringComparison.Ordinal) &&
                    b.State != BindingState.Active);
                if (nonActive is not null)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.InactiveBinding,
                        $"Slot '{slot.SlotName}' has no active binding; the existing binding is {nonActive.State}. Create or restore an active binding for this integration.",
                        slot.SlotName,
                        nonActive.Id.Value,
                        nonActive.ConnectionId?.Value));
                }
                else
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.MissingSlotBinding,
                        $"Required slot '{slot.SlotName}' has no active binding for this integration. Bind it under this integration (contract: {slot.ContractType}).",
                        slot.SlotName,
                        null,
                        null));
                }

                continue;
            }

            if (list.Count > 1)
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.DuplicateActiveBindingForSlot,
                    $"Slot '{slot.SlotName}' has multiple active bindings for this integration; revoke duplicates so only one active binding remains.",
                    slot.SlotName,
                    null,
                    null));
                continue;
            }

            var binding = list[0];
            if (!string.Equals(binding.ContractType, slot.ContractType, StringComparison.Ordinal))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingContractMismatch,
                    $"Binding for slot '{slot.SlotName}' uses contract '{binding.ContractType}' but the module requires '{slot.ContractType}'.",
                    slot.SlotName,
                    binding.Id.Value,
                    binding.ConnectionId?.Value));
            }

            if (binding.ConnectionId is { } connId)
            {
                boundConnections.TryGetValue(connId.Value, out var connection);
                if (connection is null)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.ConnectionMissing,
                        $"Binding for slot '{slot.SlotName}' references connection '{connId.Value}', which was not found.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else if (connection.State != ConnectionState.Active)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.ConnectionInactive,
                        $"Connection '{connection.Name}' ({connId.Value}) for slot '{slot.SlotName}' is not Active.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else
                {
                    AddConnectionVerificationIssueIfNeeded(issues, integration.ModuleId, slot.SlotName, binding, connection);

                    var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                        integration.ModuleId,
                        slot.SlotName,
                        slot.ContractType,
                        integration.ResourceContextId,
                        cancellationToken).ConfigureAwait(false);
                    if (!eligible.Any(c => c.Id.Value == connId.Value))
                    {
                        issues.Add(NewBlockingIssue(
                            IntegrationReadinessIssueCodes.BindingNotEligible,
                            $"Connection '{connection.Name}' is not eligible for slot '{slot.SlotName}' with contract '{slot.ContractType}' (connector or scope mismatch). Choose an eligible connection or fix the connection configuration.",
                            slot.SlotName,
                            binding.Id.Value,
                            connId.Value));
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(binding.CapabilityId))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingMissingConnectionOrCapability,
                    $"Binding for slot '{slot.SlotName}' has neither a connection nor a capability reference.",
                    slot.SlotName,
                    binding.Id.Value,
                    null));
            }
        }
    }

    private void AddConnectionVerificationIssueIfNeeded(
        List<IntegrationReadinessIssue> issues,
        string moduleId,
        string slotName,
        Binding binding,
        Connection connection)
    {
        IntegrationReadinessIssue? issue = connection.LastVerificationStatus switch
        {
            ConnectionVerificationStatus.Failed => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationFailed,
                $"Connection '{connection.Name}' last verification failed: {connection.LastVerificationSummary ?? "No summary provided."}",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            ConnectionVerificationStatus.NeverTested => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationUnverified,
                $"Connection '{connection.Name}' has not been verified yet.",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            ConnectionVerificationStatus.Unavailable => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationUnavailable,
                $"Connection '{connection.Name}' cannot be verified by this connector.",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            _ => null
        };

        if (issue is not null)
            issues.Add(issue);
    }

    private static IntegrationReadinessIssue NewBlockingIssue(
        string code,
        string message,
        string? slotName,
        string? bindingId,
        string? connectionId) =>
        new(code, message, slotName, bindingId, connectionId, IntegrationReadinessIssueSeverity.Blocking.ToString());
}
