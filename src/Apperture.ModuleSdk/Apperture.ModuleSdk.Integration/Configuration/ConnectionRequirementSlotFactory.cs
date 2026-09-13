using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Adapts connection requirements into <see cref="ICapabilitySlot"/> declarations.</summary>
public sealed record ConnectionRequirementSlot(string ModuleId, string SlotName, string ContractType) : ICapabilitySlot;

public static class ConnectionRequirementSlotFactory
{
    public static IReadOnlyList<ICapabilitySlot> ToCapabilitySlots(
        string moduleId,
        IEnumerable<ConnectionRequirementDefinition> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(requirements);

        return requirements
            .Select(r => (ICapabilitySlot)new ConnectionRequirementSlot(moduleId, r.Key, r.CapabilityContractType))
            .ToList();
    }
}
