namespace Apperture.Management.Host;

/// <summary>
/// An origin compared as scheme, host and port. No path, query, suffix or wildcard.
/// </summary>
internal readonly struct BrowserOrigin : IEquatable<BrowserOrigin>
{
    public BrowserOrigin(string scheme, string host, int port)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
    }

    public string Scheme { get; }

    public string Host { get; }

    public int Port { get; }

    public static bool TryParse(string? value, out BrowserOrigin origin)
    {
        origin = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Contains('\\', StringComparison.Ordinal)
            || text.Contains(' ', StringComparison.Ordinal)
            || text.Contains('\t', StringComparison.Ordinal)
            || text.Contains('\r', StringComparison.Ordinal)
            || text.Contains('\n', StringComparison.Ordinal)
            || text.Contains('@', StringComparison.Ordinal)
            || text.Contains('?', StringComparison.Ordinal)
            || text.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host) || uri.HostNameType == UriHostNameType.Unknown)
            return false;

        if (uri.AbsolutePath is not "" and not "/")
            return false;

        origin = new BrowserOrigin(uri.Scheme, uri.IdnHost, uri.Port);
        return true;
    }

    public bool Equals(BrowserOrigin other) =>
        string.Equals(Scheme, other.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
        && Port == other.Port;

    public override bool Equals(object? obj) => obj is BrowserOrigin other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Scheme), StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port);

    public override string ToString() => $"{Scheme}://{Host}:{Port}";
}
