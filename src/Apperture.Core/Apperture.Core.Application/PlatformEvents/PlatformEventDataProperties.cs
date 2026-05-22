namespace Apperture.Core.Application.PlatformEvents;

/// <summary>JSON property names for schedule expected-run platform event payloads.</summary>
public static class PlatformEventDataProperties
{
    public const string EvaluationStatus = "evaluationStatus";
    public const string OperatorSummaryLine = "operatorSummaryLine";
    public const string MostRecentExpectedFireUtc = "mostRecentExpectedFireUtc";
    public const string ReferenceScheduleId = "referenceScheduleId";
    public const string MissingMinutes = "missingMinutes";
    public const string ResolutionReason = "resolutionReason";
    public const string AttentionIncidentId = "attentionIncidentId";

    public const string ExecutionKind = "executionKind";
    public const string DefinitionId = "definitionId";
    public const string Status = "status";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";
    public const string FailureMessageExcerpt = "failureMessageExcerpt";
    public const string DurationMs = "durationMs";
    public const string StepCount = "stepCount";
    public const string FailedStepName = "failedStepName";
    public const string ExceptionType = "exceptionType";
}
