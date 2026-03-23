using Apperture.ModuleSdk.Abstractions;

namespace Unisa.CampusNexus.PaymentPlans;

internal sealed record CapabilitySlotDeclaration(string ModuleId, string SlotName, string ContractType) : ICapabilitySlot;
