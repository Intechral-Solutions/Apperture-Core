namespace Apperture.Persistence.SqlServer;

public sealed class EnrollmentTokenEntity
{
    public Guid Id { get; set; }
    public string Token { get; set; } = "";
    public string RuntimeId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
