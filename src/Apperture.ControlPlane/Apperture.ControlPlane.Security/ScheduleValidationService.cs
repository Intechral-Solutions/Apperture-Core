using System.Text.Json;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Scheduling;
using Cronos;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates schedule create/update inputs. Epic 38.
/// Validates cron (Cronos), timezone (IANA), target config shape.
/// </summary>
public interface IScheduleValidationService
{
    /// <summary>
    /// Validates create request. Returns errors list when invalid.
    /// </summary>
    ScheduleValidationResult ValidateCreate(string name, string recurrenceExpression, string? timeZone, bool enabled, int? targetJobType, string? targetJobConfiguration);

    /// <summary>
    /// Validates update request. Only validates fields that are provided (non-null).
    /// </summary>
    ScheduleValidationResult ValidateUpdate(string? name, string? recurrenceExpression, string? timeZone, bool? enabled);
}

public sealed record ScheduleValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ScheduleValidationResult Ok() => new(true, []);
    public static ScheduleValidationResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>
/// Epic 38: Truthful validation of schedule inputs before persist.
/// </summary>
public sealed class ScheduleValidationService : IScheduleValidationService
{
    public ScheduleValidationResult ValidateCreate(string name, string recurrenceExpression, string? timeZone, bool enabled, int? targetJobType, string? targetJobConfiguration)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Name is required.");

        var jobType = (ScheduledJobType)(targetJobType ?? 0);
        if (jobType != ScheduledJobType.WorkflowExecution && jobType != ScheduledJobType.ModuleOperationExecution)
            errors.Add("TargetJobType must be 0 (Workflow) or 1 (Operation).");

        ValidateTargetConfiguration(jobType, targetJobConfiguration, errors);
        ValidateRecurrence(recurrenceExpression, errors);
        ValidateTimeZone(timeZone ?? "UTC", errors);

        return errors.Count == 0 ? ScheduleValidationResult.Ok() : ScheduleValidationResult.Fail([.. errors]);
    }

    public ScheduleValidationResult ValidateUpdate(string? name, string? recurrenceExpression, string? timeZone, bool? enabled)
    {
        var errors = new List<string>();

        if (name is not null && string.IsNullOrWhiteSpace(name))
            errors.Add("Name cannot be empty.");

        if (recurrenceExpression is not null)
            ValidateRecurrence(recurrenceExpression, errors);

        if (timeZone is not null)
            ValidateTimeZone(timeZone, errors);

        return errors.Count == 0 ? ScheduleValidationResult.Ok() : ScheduleValidationResult.Fail([.. errors]);
    }

    private static void ValidateTargetConfiguration(ScheduledJobType jobType, string? targetJobConfiguration, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(targetJobConfiguration))
        {
            errors.Add("Target configuration is required.");
            return;
        }

        try
        {
            var doc = JsonDocument.Parse(targetJobConfiguration);
            var root = doc.RootElement;

            if (jobType == ScheduledJobType.WorkflowExecution)
            {
                if (!root.TryGetProperty("workflowDefinitionId", out var wf) || wf.GetString() is not { Length: > 0 })
                    errors.Add("Target configuration must contain workflowDefinitionId for workflow schedules.");
            }
            else if (jobType == ScheduledJobType.ModuleOperationExecution)
            {
                if (!root.TryGetProperty("operationDefinitionId", out var op) || op.GetString() is not { Length: > 0 })
                    errors.Add("Target configuration must contain operationDefinitionId for operation schedules.");
            }
        }
        catch (JsonException)
        {
            errors.Add("Target configuration must be valid JSON.");
        }
    }

    private static void ValidateRecurrence(string recurrenceExpression, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(recurrenceExpression))
        {
            errors.Add("Recurrence expression is required.");
            return;
        }

        try
        {
            _ = CronExpression.Parse(recurrenceExpression);
        }
        catch (CronFormatException ex)
        {
            errors.Add($"Invalid cron expression: {ex.Message}");
        }
    }

    private static void ValidateTimeZone(string timeZone, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            errors.Add("Time zone is required.");
            return;
        }

        if (!ScheduleTimeZoneResolver.TryFind(timeZone, out _))
            errors.Add($"Unknown time zone: {timeZone}. Use IANA IDs (e.g. UTC, America/Los_Angeles).");
    }
}
