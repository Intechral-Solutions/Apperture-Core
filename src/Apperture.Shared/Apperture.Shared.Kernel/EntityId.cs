namespace Apperture.Shared.Kernel;

public abstract record EntityId(string Value)
{
    public override string ToString() => Value;
}
