using Apperture.RuntimePlane.Abstractions;

namespace Apperture.RuntimePlane.Engine;

public sealed class WorkflowValidationService
{
    public bool Validate(WorkflowDefinition definition)
    {
        if (definition.StepNames.Count == 0)
            return false;
        return true;
    }
}
