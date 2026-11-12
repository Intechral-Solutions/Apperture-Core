namespace Apperture.Core.Application.Services;

/// <summary>
/// A write to a user's record was made from a copy whose Recovery Access designation no longer matches the stored one.
/// </summary>
/// <remarks>
/// The designation is decided by the Recovery Access service alone, so a writer that read a user earlier must not carry its
/// copy of the designation past a designation or a revocation that committed in between. The repository reads the stored
/// designation under a row lock in the writer's own transaction and raises this when it differs from the writer's copy; nothing is
/// written. The caller maps it to the refusal that site already has for a designated account (<see cref="IsDesignated"/>), or to a
/// retryable stale-update refusal when the account is no longer designated. It carries no user data.
/// </remarks>
public sealed class UserDesignationChangedException : Exception
{
    public UserDesignationChangedException(bool isDesignated)
        : base("The account's Recovery Access designation changed while this was being processed. Reload and try again.") =>
        IsDesignated = isDesignated;

    /// <summary>The stored designation: true when the account is designated now, false when it no longer is.</summary>
    public bool IsDesignated { get; }
}
