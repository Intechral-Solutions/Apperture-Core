using System.Text.Json.Serialization;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Audit action names for the Connection lifecycle. Past tense on purpose: these name what happened, and are
/// distinct from the <c>connection.create</c>/<c>update</c>/<c>delete</c> permission actions.
/// </summary>
public static class ConnectionAuditActionNames
{
    public const string Created = "connection.created";
    public const string Updated = "connection.updated";
    public const string SecretRotated = "connection.secret_rotated";
    public const string Retired = "connection.retired";
    public const string Verified = "connection.verified";

    /// <summary>One remote structure request against the external system a Connection points at (one record per request).</summary>
    public const string StructureBrowsed = "connection.structure_browsed";
}

/// <summary>
/// Structured JSON <c>Details</c> for a Connection lifecycle audit record. Connection identity lives here
/// (<c>resourceType</c>/<c>resourceId</c>) rather than in <c>AuditRecord.Resource</c>, which the audit store does not
/// persist and which would corrupt JSON details if set.
/// <para>
/// By construction this carries only identity, field <b>keys</b> and a verification status. It never carries a config
/// value, a secret value, a secret reference or any remote verification payload.
/// </para>
/// </summary>
public sealed record ConnectionAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("connectorDefinitionId")] string ConnectorDefinitionId,
    [property: JsonPropertyName("nameChanged"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NameChanged = null,
    [property: JsonPropertyName("configFieldsChanged"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ConfigFieldsChanged = null,
    [property: JsonPropertyName("secretFieldsRotated"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? SecretFieldsRotated = null,
    [property: JsonPropertyName("verificationStatus"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VerificationStatus = null);

/// <summary>
/// Structured JSON <c>Details</c> for one remote tool operation. It records that an operator ran a metadata operation
/// against a Connection and how it ended, with counts. It never carries the names, types or any other CONTENT the remote
/// system returned, the schema or table the operator selected, a secret, a connection string or remote error text.
/// </summary>
public sealed record ConnectionToolAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("connectorDefinitionId")] string ConnectorDefinitionId,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("toolOutcome")] string ToolOutcome,
    [property: JsonPropertyName("resultCount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ResultCount = null,
    [property: JsonPropertyName("truncated"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Truncated = null);
