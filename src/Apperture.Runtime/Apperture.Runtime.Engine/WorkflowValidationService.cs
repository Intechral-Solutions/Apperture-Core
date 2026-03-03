using Apperture.Runtime.Abstractions;

namespace Apperture.Runtime.Engine;

public sealed class WorkflowValidationService
{
    public bool Validate(WorkflowDefinition definition)
    {
        if (definition.StepNames.Count == 0)
            return false;
        return true;
    }
}
