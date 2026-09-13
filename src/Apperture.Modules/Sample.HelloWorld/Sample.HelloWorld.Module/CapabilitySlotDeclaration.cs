using Apperture.ModuleSdk.Abstractions;

namespace Sample.HelloWorld;

internal sealed record CapabilitySlotDeclaration(string ModuleId, string SlotName, string ContractType) : ICapabilitySlot;
