namespace Apperture.Core.Application.Execution;

/// <summary>
/// Narrow optional module hook for instance-plane step handler registration.
/// Implement on module types so the host can populate <see cref="IStepHandlerRegistry"/> without hard-coded registrations.
/// </summary>
public interface IModuleStepHandlerRegistrar
{
    void RegisterStepHandlers(IStepHandlerRegistry registry);
}

