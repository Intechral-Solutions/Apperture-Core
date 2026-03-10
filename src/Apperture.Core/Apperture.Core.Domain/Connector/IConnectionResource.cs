using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Governed connection resource. Exposes identity for authorization; no secret material.
/// </summary>
public interface IConnectionResource : IGovernedResource;
