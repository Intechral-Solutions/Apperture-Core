namespace Apperture.Core.Application.Execution;

/// <summary>
/// Thrown when a step requires a slot-bound dependency but the slot has no active connection binding
/// (or resolution returned null). Step handlers may throw this after checking ResolveSlotAsync for null.
/// </summary>
public sealed class SlotNotBoundException : InvalidOperationException
{
    /// <summary>
    /// Module id for the slot.
    /// </summary>
    public string ModuleId { get; }

    /// <summary>
    /// Slot name that could not be resolved.
    /// </summary>
    public string SlotName { get; }

    public SlotNotBoundException(string moduleId, string slotName)
        : base($"Slot '{slotName}' (module '{moduleId}') is not bound or connection is unavailable.")
    {
        ModuleId = moduleId;
        SlotName = slotName;
    }

    public SlotNotBoundException(string moduleId, string slotName, Exception inner)
        : base($"Slot '{slotName}' (module '{moduleId}') is not bound or connection is unavailable.", inner)
    {
        ModuleId = moduleId;
        SlotName = slotName;
    }
}
