namespace Apperture.Management.Ui.Client.Services.Wizard;

/// <summary>
/// Reusable wizard flow controller with deterministic step gating.
/// Pages provide current <see cref="WizardStepState"/> projection from server truth.
/// </summary>
public sealed class WizardFlowController
{
    private readonly IReadOnlyList<WizardStepDefinition> _steps;
    private readonly Dictionary<string, WizardStepDefinition> _byId;
    private Dictionary<string, WizardStepState> _states;

    public WizardFlowController(IReadOnlyList<WizardStepDefinition> steps, string? initialStepId = null)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        if (_steps.Count == 0) throw new ArgumentException("Wizard must have at least one step.", nameof(steps));
        _byId = _steps.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _states = _steps.ToDictionary(
            x => x.Id,
            x => new WizardStepState(x.Id, WizardStepStatus.Pending),
            StringComparer.Ordinal);
        CurrentStepId = _byId.ContainsKey(initialStepId ?? "") ? initialStepId! : _steps[0].Id;
    }

    public string CurrentStepId { get; private set; }

    public IReadOnlyList<WizardStepDefinition> Steps => _steps;

    public void SetStates(IReadOnlyList<WizardStepState> states)
    {
        _states = states
            .Where(s => _byId.ContainsKey(s.StepId))
            .ToDictionary(s => s.StepId, StringComparer.Ordinal);
    }

    public WizardStepState GetState(string stepId)
    {
        if (_states.TryGetValue(stepId, out var state))
            return state;
        return new WizardStepState(stepId, WizardStepStatus.Pending);
    }

    public bool CanNavigateTo(string stepId)
    {
        if (!_byId.ContainsKey(stepId))
            return false;
        var state = GetState(stepId);
        return state.Status is not WizardStepStatus.Blocked;
    }

    public bool NavigateTo(string stepId)
    {
        if (!CanNavigateTo(stepId))
            return false;
        CurrentStepId = stepId;
        return true;
    }

    public bool TryMoveNext()
    {
        var idx = IndexOf(CurrentStepId);
        if (idx < 0 || idx >= _steps.Count - 1)
            return false;
        return NavigateTo(_steps[idx + 1].Id);
    }

    public bool TryMovePrevious()
    {
        var idx = IndexOf(CurrentStepId);
        if (idx <= 0)
            return false;
        CurrentStepId = _steps[idx - 1].Id;
        return true;
    }

    public WizardStepDefinition? Current => _byId.TryGetValue(CurrentStepId, out var step) ? step : null;

    private int IndexOf(string stepId) =>
        _steps
            .Select((step, index) => new { step.Id, index })
            .FirstOrDefault(x => string.Equals(x.Id, stepId, StringComparison.Ordinal))
            ?.index ?? -1;
}
