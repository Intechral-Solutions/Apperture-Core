namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Declared consumer slot identity. A module declares slots it requires;
/// bindings delegate connections or capabilities to these slots.
/// </summary>
public interface ICapabilitySlot
{
    /// <summary>Module that declares this slot.</summary>
    string ModuleId { get; }

    /// <summary>Slot name within the module.</summary>
    string SlotName { get; }

    /// <summary>Contract type (e.g. fully-qualified interface name) the slot expects.</summary>
    string ContractType { get; }
}
