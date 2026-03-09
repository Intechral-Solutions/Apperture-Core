namespace Apperture.Shared.Kernel;

/// <summary>
/// Stable identifier for an actor (human, machine, or system principal) used in audit and authorization.
/// </summary>
public sealed record ActorId(string Value)
{
    public override string ToString() => Value;
}
