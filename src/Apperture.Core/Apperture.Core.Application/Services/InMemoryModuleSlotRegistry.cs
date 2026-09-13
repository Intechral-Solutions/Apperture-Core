using System.Collections.Concurrent;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// In-memory module slot registry for runtime hosts and tests.
/// </summary>
public sealed class InMemoryModuleSlotRegistry : IModuleSlotRegistry
{
    private readonly ConcurrentDictionary<string, List<ICapabilitySlot>> _byModule = new(StringComparer.Ordinal);

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
    public IReadOnlyList<ICapabilitySlot> GetSlotsForModule(string moduleId) =>
        _byModule.TryGetValue(moduleId, out var slots) ? slots : [];
}
