using System.Net;
using System.Net.Sockets;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// The five origins the harness uses. Ports are allocated dynamically (the developer's Aspire stack owns
/// :9000/:9001), but the <em>hosts</em> are fixed on purpose because browser SameSite semantics depend on
/// the site (scheme + registrable host), not the port:
/// <list type="table">
/// <item><term>UI</term><description><c>http://localhost:{ui}</c> — Ui.Server child process.</description></item>
/// <item><term>API</term><description><c>http://localhost:{api}</c> — Management.Host. Same site as the UI (cross-origin, same-site), exactly like the dev topology.</description></item>
/// <item><term>OIDC</term><description><c>http://127.0.0.1:{oidc}</c> — stub identity provider. A different <em>site</em> from <c>localhost</c>, so its <c>form_post</c> back to the API is genuinely cross-site.</description></item>
/// <item><term>Cross-site attacker</term><description><c>http://127.0.0.1:{attacker}</c> — different site from UI/API.</description></item>
/// <item><term>Same-site attacker</term><description><c>http://localhost:{sibling}</c> — different origin, same site as UI/API (a "sibling" origin).</description></item>
/// </list>
/// </summary>
public sealed record HarnessOrigins(
    int UiPort,
    int ApiPort,
    int OidcPort,
    int CrossSiteAttackerPort,
    int SameSiteAttackerPort)
{
    public string Ui => $"http://localhost:{UiPort}";

    public string Api => $"http://localhost:{ApiPort}";

    public string Oidc => $"http://127.0.0.1:{OidcPort}";

    public string CrossSiteAttacker => $"http://127.0.0.1:{CrossSiteAttackerPort}";

    public string SameSiteAttacker => $"http://localhost:{SameSiteAttackerPort}";

    public static HarnessOrigins AllocateDynamic()
    {
        var ports = new HashSet<int>();
        while (ports.Count < 5)
            ports.Add(FreeLoopbackPort());

        var p = ports.ToArray();
        return new HarnessOrigins(p[0], p[1], p[2], p[3], p[4]);
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
