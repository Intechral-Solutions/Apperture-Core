namespace Apperture.ModuleSdk.Integration.Readiness;

/// <summary>Integration-level configuration readiness projection.</summary>
public sealed record ConfigurationReadinessResult(
    ConfigurationReadinessState State,
    IReadOnlyList<ConfigurationReadinessReason> Reasons,
    DateTimeOffset EvaluatedAtUtc)
{
    public bool IsReady => State == ConfigurationReadinessState.Ready;

    public bool IsExecutable =>
        State is ConfigurationReadinessState.Ready or ConfigurationReadinessState.Degraded;

    public static ConfigurationReadinessResult Ready(DateTimeOffset evaluatedAtUtc) =>
        new(ConfigurationReadinessState.Ready, Array.Empty<ConfigurationReadinessReason>(), evaluatedAtUtc);

    public static ConfigurationReadinessResult FromReasons(
        IEnumerable<ConfigurationReadinessReason> reasons,
        DateTimeOffset evaluatedAtUtc)
    {
        var list = reasons.ToList();
        var hasBlocking = list.Any(r => r.Severity == ConfigurationReadinessSeverity.Blocking);
        var hasAdvisory = list.Any(r => r.Severity == ConfigurationReadinessSeverity.Advisory);
        var state = hasBlocking
            ? ConfigurationReadinessState.NotReady
            : hasAdvisory
                ? ConfigurationReadinessState.Degraded
                : ConfigurationReadinessState.Ready;
        return new ConfigurationReadinessResult(state, list, evaluatedAtUtc);
    }
}

/// <summary>Procedure-specific readiness (subset of integration requirements).</summary>
public sealed record ProcedureReadinessResult(
    string ProcedureId,
    ConfigurationReadinessState State,
    IReadOnlyList<ConfigurationReadinessReason> Reasons,
    DateTimeOffset EvaluatedAtUtc)
{
    public bool IsExecutable =>
        State is ConfigurationReadinessState.Ready or ConfigurationReadinessState.Degraded;

    public static ProcedureReadinessResult FromIntegration(
        string procedureId,
        ConfigurationReadinessResult integration) =>
        new(procedureId, integration.State, integration.Reasons, integration.EvaluatedAtUtc);

    public static ProcedureReadinessResult FromReasons(
        string procedureId,
        IEnumerable<ConfigurationReadinessReason> reasons,
        DateTimeOffset evaluatedAtUtc)
    {
        var integration = ConfigurationReadinessResult.FromReasons(reasons, evaluatedAtUtc);
        return new ProcedureReadinessResult(procedureId, integration.State, integration.Reasons, evaluatedAtUtc);
    }
}
