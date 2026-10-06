namespace Apperture.Core.Application.Services;

/// <summary>
/// What a username change did, as reported by the transaction that made it. <see cref="PreviousUsername"/> is the name
/// actually replaced and <see cref="Username"/> the canonical stored form after the call; they are equal when
/// <see cref="Outcome"/> is <see cref="UsernameChangeOutcome.Unchanged"/>.
/// </summary>
public sealed record UsernameRenameResult(UsernameChangeOutcome Outcome, string PreviousUsername, string Username);
