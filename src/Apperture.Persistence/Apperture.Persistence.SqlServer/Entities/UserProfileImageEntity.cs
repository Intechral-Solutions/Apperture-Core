namespace Apperture.Persistence.SqlServer;

/// <summary>
/// One normalized, platform-re-encoded avatar rendition. Only derived images are stored — the
/// original upload is never persisted.
/// </summary>
public sealed class UserProfileImageEntity
{
    /// <summary>Surrogate id; the meaningful identity is (<see cref="ImageKey"/>, <see cref="Size"/>).</summary>
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    /// <summary>Platform-generated logical key shared by every rendition of one avatar.</summary>
    public string ImageKey { get; set; } = "";

    /// <summary>Square edge length in pixels.</summary>
    public int Size { get; set; }

    public string ContentType { get; set; } = "";

    public byte[] Content { get; set; } = [];

    public int ByteLength { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
