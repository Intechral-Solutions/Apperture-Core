namespace Apperture.ModuleSdk.Abstractions;

public interface IModuleActivationFilter
{
    ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default);
}
