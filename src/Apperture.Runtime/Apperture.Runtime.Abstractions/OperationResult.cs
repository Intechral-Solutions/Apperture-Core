namespace Apperture.Runtime.Abstractions;

public sealed record OperationResult(bool Success, string? ErrorMessage, object? Payload);
