using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Execution;

public static class ExecutionTargetKinds
{
    public const string Workflow = "workflow";
    public const string Operation = "operation";
    public const string Procedure = "procedure";
}

public static class ExecutionRendererKeys
{
    public const string MetadataForm = "metadata-form";
    public const string RawJson = "raw-json";
}

public sealed record ExecutionInputField(
    string Key,
    string Type,
    string Label,
    string? HelpText,
    bool Required,
    string? DefaultValue,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record ExecutionDescriptor(
    string Kind,
    string IntegrationId,
    string TargetId,
    string DisplayName,
    bool Launchable,
    IReadOnlyList<string> BlockedReasonCodes,
    string RendererKey,
    string? WizardDefinitionRef = null,
    IReadOnlyList<ExecutionInputField>? Fields = null,
    IntegrationProcedureDetailDto? ProcedureDetail = null);

public sealed record ExecutionLaunchOutcome(
    bool Succeeded,
    string? RunId,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage);
