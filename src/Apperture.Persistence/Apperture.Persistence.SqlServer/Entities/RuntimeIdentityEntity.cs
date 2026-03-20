namespace Apperture.Persistence.SqlServer;

public sealed class RuntimeIdentityEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CredentialType { get; set; } = "";
    public string? PublicKeyOrThumbprint { get; set; }
    public string? ManagedIdentityPrincipalId { get; set; }
    public string DeclaredScopesJson { get; set; } = "[]";
    public DateTimeOffset RegisteredAt { get; set; }
}
