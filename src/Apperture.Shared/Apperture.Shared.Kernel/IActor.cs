namespace Apperture.Shared.Kernel;

/// <summary>
/// Represents an authenticated principal (human, machine, or system) for audit and authorization.
/// </summary>
public interface IActor
{
    ActorId Id { get; }
    ActorKind Kind { get; }
    string? DisplayName { get; }
}

/// <summary>
/// Kind of actor for authorization and audit.
/// </summary>
public enum ActorKind
{
    Human,
    Machine,
    System
}
