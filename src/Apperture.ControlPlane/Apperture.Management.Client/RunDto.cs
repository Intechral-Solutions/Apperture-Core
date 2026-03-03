namespace Apperture.Management.Client;

public sealed record RunDto(string Id, string WorkflowInstanceId, string Status, DateTimeOffset CreatedAt);
