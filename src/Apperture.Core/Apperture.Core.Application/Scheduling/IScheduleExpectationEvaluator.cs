namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Evaluates whether scheduled fires were due and satisfied by schedule-origin runs (Epic 65).
/// </summary>
public interface IScheduleExpectationEvaluator
{
    ScheduleExpectationEvaluation Evaluate(ScheduleExpectationEvaluationContext context);
}
