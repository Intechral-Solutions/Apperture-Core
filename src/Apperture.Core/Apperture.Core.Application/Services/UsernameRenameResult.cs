namespace Apperture.Core.Application.Services;

/// <summary>
/// What an administrator rename did. <see cref="Username"/> is the canonical stored form after the call, and
/// equals <see cref="PreviousUsername"/> when <see cref="Outcome"/> is <see cref="UsernameChangeOutcome.Unchanged"/>.
/// </summary>
public sealed record UsernameRenameResult(UsernameChangeOutcome Outcome, string PreviousUsername, string Username);
