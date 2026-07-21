using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registry of declared capability slots. Modules and platform register slots at startup.
/// </summary>
public interface IModuleSlotRegistry
{
    /// <summary>
    /// Registers a capability slot.
    /// </summary>
    void RegisterSlot(ICapabilitySlot slot);

    /// <summary>
    /// Gets all slots declared for a module.
    /// </summary>
    IReadOnlyList<ICapabilitySlot> GetSlotsForModule(string moduleId);
}
