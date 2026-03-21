namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// Execution-related surfaces that remain partially composed until process restart.
/// See Epic 27 execution surface inventory.
/// </summary>
public enum RestartBoundSurface
{
    /// <summary>Step handlers registered at worker startup from all DI modules; not unregistered when a module is disabled.</summary>
    StepHandlerRegistry = 0,

    /// <summary>Module-declared permissions registered into <c>IResourceDescriptorRegistry</c> at control-plane process start.</summary>
    ModulePermissionRegistration = 1
}
