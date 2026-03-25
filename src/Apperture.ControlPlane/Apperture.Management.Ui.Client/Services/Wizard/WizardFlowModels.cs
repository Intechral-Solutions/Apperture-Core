namespace Apperture.Management.Ui.Client.Services.Wizard;

public enum WizardStepStatus
{
    Pending,
    Current,
    Complete,
    Blocked
}

public sealed record WizardStepDefinition(
    string Id,
    string Title,
    string? Description = null);

public sealed record WizardStepState(
    string StepId,
    WizardStepStatus Status,
    string? BlockedReason = null);
