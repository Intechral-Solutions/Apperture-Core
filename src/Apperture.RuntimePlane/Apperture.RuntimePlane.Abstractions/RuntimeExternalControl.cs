namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// External control request kinds the runtime recipient model can accept.
/// </summary>
public static class RuntimeControlKindNames
{
    public const string Shutdown = "shutdown";
}

/// <summary>
/// Durable lifecycle state for queued runtime external control requests.
/// </summary>
public static class RuntimeControlStatusNames
{
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
}

