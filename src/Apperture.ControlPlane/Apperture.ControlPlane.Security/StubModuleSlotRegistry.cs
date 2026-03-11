using System.Collections.Concurrent;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// In-memory module slot registry. For wiring and tests; replace when module manifests provide slots.
/// </summary>
public sealed class StubModuleSlotRegistry : IModuleSlotRegistry
{
    private readonly ConcurrentDictionary<string, List<ICapabilitySlot>> _byModule = new();

    /// <inheritdoc />
    public void RegisterSlot(ICapabilitySlot slot)
    {
        _byModule.AddOrUpdate(
            slot.ModuleId,
            _ => [slot],
            (_, list) =>
            {
                var existing = list.FindIndex(s => s.SlotName == slot.SlotName);
                var newList = new List<ICapabilitySlot>(list);
                if (existing >= 0)
                    newList[existing] = slot;
                else
                    newList.Add(slot);
                return newList;
            });
    }

    /// <inheritdoc />
    public IReadOnlyList<ICapabilitySlot> GetSlotsForModule(string moduleId)
    {
        return _byModule.TryGetValue(moduleId, out var slots)
            ? slots
            : [];
    }
}
