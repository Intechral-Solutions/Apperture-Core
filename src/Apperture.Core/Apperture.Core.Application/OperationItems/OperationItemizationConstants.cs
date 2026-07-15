namespace Apperture.Core.Application.OperationItems;

public static class OperationGroupStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Failed = "Failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Running,
        Succeeded,
        CompletedWithErrors,
        Failed,
    };

    public static bool IsTerminal(string status) =>
        status is Succeeded or CompletedWithErrors or Failed;
}

public static class OperationItemStatuses
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending,
        Succeeded,
        Failed,
        Skipped,
    };
}

/// <summary>Size and length bounds for durable operation itemization (SDK-3A).</summary>
public static class OperationItemizationBounds
{
    public const int IdMaxLength = 64;
    public const int ModuleIdMaxLength = 128;
    public const int ProcedureIdMaxLength = 128;
    public const int OperationKindMaxLength = 64;
    public const int SourceLabelMaxLength = 256;
    public const int SubjectKeyMaxLength = 256;
    public const int DisplayLabelMaxLength = 256;
    public const int ErrorCodeMaxLength = 64;
    public const int ErrorMessageMaxLength = 1024;
    public const int SummaryJsonMaxChars = 2048;
    public const int ChangeSummaryJsonMaxChars = 2048;
    public const int MetadataJsonMaxChars = 4096;
}
