namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// External control request kinds the runtime recipient model can accept.
/// </summary>
public static class WorkflowExternalControlKindNames
{
    public const string Park = "park";
    public const string Resume = "resume";
}

/// <summary>
/// Durable lifecycle state for queued external control requests.
/// </summary>
public static class WorkflowExternalControlStatusNames
{
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
}
