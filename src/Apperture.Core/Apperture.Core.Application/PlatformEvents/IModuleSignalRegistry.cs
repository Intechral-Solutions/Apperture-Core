using System.Diagnostics.CodeAnalysis;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Resolved manifest signal with derived platform catalog descriptor (Epic 77 Phase 2).</summary>
public sealed record ModuleSignalRegistration(
    string ModuleId,
    string SignalName,
    ModuleSignalDeclaration Declaration,
    PlatformEventTypeDescriptor Descriptor);

/// <summary>In-memory registry of module signal declarations built at worker startup.</summary>
public interface IModuleSignalRegistry
{
    bool TryResolve(string moduleId, string signalName, [NotNullWhen(true)] out ModuleSignalRegistration? registration);

    IReadOnlyDictionary<string, PlatformEventTypeDescriptor> ModuleDescriptorsByType { get; }
}
