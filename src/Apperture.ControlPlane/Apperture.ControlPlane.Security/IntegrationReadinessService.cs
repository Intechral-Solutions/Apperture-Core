using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class IntegrationReadinessService : IIntegrationReadinessService
{
    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IModuleSlotRegistry _moduleSlotRegistry;
    private readonly IBindingRepository _bindingRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IBindingEligibilityService _eligibilityService;
    private readonly IScheduleRepository _scheduleRepository;

    public IntegrationReadinessService(
        IIntegrationManagementService integrationManagement,
        IModuleSlotRegistry moduleSlotRegistry,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IScheduleRepository scheduleRepository)
    {
        _integrationManagement = integrationManagement;
        _moduleSlotRegistry = moduleSlotRegistry;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _scheduleRepository = scheduleRepository;
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationReadinessProjection?> GetReadinessAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        return await EvaluateReadinessAsync(integration, bindings, schedules, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationOperatorDetailProjection?> GetOperatorDetailAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var readiness = await EvaluateReadinessAsync(integration, bindings, schedules, cancellationToken);

        var slots = _moduleSlotRegistry.GetSlotsForModule(integration.ModuleId);
        var requiredSlots = slots
            .Select(s => new IntegrationModuleSlotInfo(s.SlotName, s.ContractType))
            .ToList();

        var bindingSummaries = bindings
            .Select(b => new IntegrationBindingSummary(
                b.Id.Value,
                b.SlotName,
                b.ContractType,
                b.ConnectionId?.Value,
                b.CapabilityId,
                b.State.ToString()))
            .ToList();

        var scheduleSummaries = schedules
            .Select(d => new IntegrationScheduleSummary(
                d.Id.Value,
                d.Name,
                d.Enabled,
                d.RecurrenceExpression,
                d.IntegrationId?.Value))
            .ToList();

        return new IntegrationOperatorDetailProjection(
            integration.Id.Value,
            integration.DisplayName,
            integration.ModuleId,
            integration.ScopeKey,
            integration.State.ToString(),
            integration.Settings,
            integration.CreatedAt,
            integration.UpdatedAt,
            readiness,
            requiredSlots,
            bindingSummaries,
            scheduleSummaries);
    }

    private async Task<IntegrationReadinessProjection> EvaluateReadinessAsync(
        DomainIntegration integration,
        IReadOnlyList<Binding> bindings,
        IReadOnlyList<ScheduleDefinition> schedules,
        CancellationToken cancellationToken)
    {
        var issues = new List<IntegrationReadinessIssue>();

        if (integration.State != IntegrationState.Active)
        {
            issues.Add(new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.IntegrationNotActive,
                $"Integration is {integration.State}; set the integration to Active before running workflows.",
                SlotName: null,
                BindingId: null,
                ConnectionId: null));
        }

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
                    issues.Add(new IntegrationReadinessIssue(
                        IntegrationReadinessIssueCodes.InactiveBinding,
                        $"Slot '{slot.SlotName}' has no active binding; the existing binding is {nonActive.State}. Create or restore an active binding for this integration.",
                        slot.SlotName,
                        nonActive.Id.Value,
                        nonActive.ConnectionId?.Value));
                }
                else
                {
                    issues.Add(new IntegrationReadinessIssue(
                        IntegrationReadinessIssueCodes.MissingSlotBinding,
                        $"Required slot '{slot.SlotName}' has no active binding for this integration. Bind it under this integration (contract: {slot.ContractType}).",
                        slot.SlotName,
                        BindingId: null,
                        ConnectionId: null));
                }

                continue;
            }

            if (list.Count > 1)
            {
                issues.Add(new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.DuplicateActiveBindingForSlot,
                    $"Slot '{slot.SlotName}' has multiple active bindings for this integration; revoke duplicates so only one active binding remains.",
                    slot.SlotName,
                    BindingId: null,
                    ConnectionId: null));
                continue;
            }

            var binding = list[0];
            if (!string.Equals(binding.ContractType, slot.ContractType, StringComparison.Ordinal))
            {
                issues.Add(new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.BindingContractMismatch,
                    $"Binding for slot '{slot.SlotName}' uses contract '{binding.ContractType}' but the module requires '{slot.ContractType}'.",
                    slot.SlotName,
                    binding.Id.Value,
                    binding.ConnectionId?.Value));
            }

            if (binding.ConnectionId is { } connId)
            {
                var connection = await _connectionRepository.GetByIdAsync(connId, cancellationToken);
                if (connection is null)
                {
                    issues.Add(new IntegrationReadinessIssue(
                        IntegrationReadinessIssueCodes.ConnectionMissing,
                        $"Binding for slot '{slot.SlotName}' references connection '{connId.Value}', which was not found.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else if (connection.State != ConnectionState.Active)
                {
                    issues.Add(new IntegrationReadinessIssue(
                        IntegrationReadinessIssueCodes.ConnectionInactive,
                        $"Connection '{connection.Name}' ({connId.Value}) for slot '{slot.SlotName}' is not Active.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else
                {
                    var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                        integration.ModuleId,
                        slot.SlotName,
                        slot.ContractType,
                        cancellationToken);
                    if (!eligible.Any(c => c.Id.Value == connId.Value))
                    {
                        issues.Add(new IntegrationReadinessIssue(
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
                issues.Add(new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.BindingMissingConnectionOrCapability,
                    $"Binding for slot '{slot.SlotName}' has neither a connection nor a capability reference.",
                    slot.SlotName,
                    binding.Id.Value,
                    ConnectionId: null));
            }
        }

        foreach (var s in schedules)
        {
            if (s.IntegrationId != integration.Id)
            {
                issues.Add(new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.ScheduleOwnershipMismatch,
                    $"Schedule '{s.Name}' ({s.Id.Value}) is not correctly associated with this integration in persisted data.",
                    SlotName: null,
                    BindingId: null,
                    ConnectionId: null));
            }
        }

        var runnable = integration.State == IntegrationState.Active && issues.Count == 0;
        return new IntegrationReadinessProjection(
            integration.Id.Value,
            integration.ModuleId,
            runnable,
            issues);
    }
}
