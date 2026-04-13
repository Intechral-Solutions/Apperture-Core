using Apperture.ModuleSdk.Abstractions;

namespace Proof.CampusNexus.StudentReview;

internal sealed record CapabilitySlotDeclaration(string ModuleId, string SlotName, string ContractType) : ICapabilitySlot;
