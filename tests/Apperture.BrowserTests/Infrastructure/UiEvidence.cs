using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>One stop of the keyboard focus order, as the browser reports it.</summary>
public sealed record FocusStop(string Tag, string Id, string Text, string? Href, bool FocusVisible, string Outline, string BoxShadow)
{
    public override string ToString() =>
        $"{Tag}{(Id.Length > 0 ? "#" + Id : "")} [{Text}]{(Href is null ? "" : " -> " + Href)} focus-visible={FocusVisible} outline='{Outline}' shadow='{BoxShadow}'";
}

/// <summary>
/// Evidence collection for a real page in real Chromium. Kept honest about what each helper is:
/// <see cref="ComputedAsync"/> is the browser's own accessibility tree (Chrome DevTools Protocol), not a screen reader;
/// <see cref="TabAsync"/> is real keyboard focus order; <see cref="ContrastAsync"/> is computed from the rendered colors;
/// screenshots are for visual inspection. None of them is assistive-technology output.
/// </summary>
public sealed class UiEvidence
{
    private readonly IPage _page;
    private readonly ICDPSession _cdp;
    private readonly string _directory;
    private readonly ITestOutputHelper _output;

    private UiEvidence(IPage page, ICDPSession cdp, string directory, ITestOutputHelper output)
    {
        _page = page;
        _cdp = cdp;
        _directory = directory;
        _output = output;
    }

    public static string DefaultDirectory()
    {
        var root = BrowserSqlConfiguration.FindRepoRoot() ?? throw new InvalidOperationException("Repository root (.git) not found.");
        return Path.Combine(root, "artifacts", "iam95-visual");
    }

    public static async Task<UiEvidence> StartAsync(IBrowserContext context, IPage page, ITestOutputHelper output)
    {
        var cdp = await context.NewCDPSessionAsync(page).ConfigureAwait(false);
        await cdp.SendAsync("DOM.enable").ConfigureAwait(false);
        await cdp.SendAsync("Accessibility.enable").ConfigureAwait(false);
        var directory = DefaultDirectory();
        Directory.CreateDirectory(directory);
        return new UiEvidence(page, cdp, directory, output);
    }

    public void Log(string category, string message)
    {
        _output.Line(category, message);
        File.AppendAllText(Path.Combine(_directory, "evidence.log"), $"[{category}] {message}{Environment.NewLine}");
    }

    /// <summary>The browser-computed role, accessible name and accessible description of the first element matching <paramref name="locator"/>.</summary>
    public async Task<(string Role, string Name, string Description)> ComputedAsync(ILocator locator)
    {
        await locator.First.EvaluateAsync("e => e.setAttribute('data-ax-probe', '1')").ConfigureAwait(false);
        try
        {
            var doc = await _cdp.SendAsync("DOM.getDocument", new Dictionary<string, object> { ["depth"] = 0 }).ConfigureAwait(false);
            var rootId = doc!.Value.GetProperty("root").GetProperty("nodeId").GetInt32();
            var query = await _cdp.SendAsync("DOM.querySelector", new Dictionary<string, object>
            {
                ["nodeId"] = rootId,
                ["selector"] = "[data-ax-probe='1']",
            }).ConfigureAwait(false);
            var nodeId = query!.Value.GetProperty("nodeId").GetInt32();
            var tree = await _cdp.SendAsync("Accessibility.getPartialAXTree", new Dictionary<string, object>
            {
                ["nodeId"] = nodeId,
                ["fetchRelatives"] = false,
            }).ConfigureAwait(false);
            var node = tree!.Value.GetProperty("nodes")[0];
            return (Value(node, "role"), Value(node, "name"), Value(node, "description"));
        }
        finally
        {
            await locator.First.EvaluateAsync("e => e.removeAttribute('data-ax-probe')").ConfigureAwait(false);
        }
    }

    private static string Value(JsonElement node, string property) =>
        node.TryGetProperty(property, out var p) && p.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";

    /// <summary>The text a sighted user reads in the element: screen-reader-only branches removed.</summary>
    public async Task<string> VisibleTextAsync(ILocator locator) =>
        await locator.First.EvaluateAsync<string>("""
            el => { const c = el.cloneNode(true); c.querySelectorAll('.sr-only').forEach(n => n.remove()); return c.textContent.trim(); }
            """).ConfigureAwait(false);

    /// <summary>What a user who selects the element's contents and copies would get, as the browser reports the selection.</summary>
    public async Task<string> SelectedTextAsync(ILocator locator) =>
        await locator.First.EvaluateAsync<string>("""
            el => { const r = document.createRange(); r.selectNodeContents(el); const s = getSelection(); s.removeAllRanges(); s.addRange(r); const t = s.toString(); s.removeAllRanges(); return t; }
            """).ConfigureAwait(false);

    /// <summary>The currently focused element, with whether the browser shows a focus indicator for it.</summary>
    public async Task<FocusStop> FocusedAsync()
    {
        var json = await _page.EvaluateAsync<string>("""
            () => {
              const e = document.activeElement;
              if (!e || e === document.body) return JSON.stringify({ tag: 'body', id: '', text: '', href: null, fv: false, outline: '', shadow: '' });
              const cs = getComputedStyle(e);
              return JSON.stringify({
                tag: e.tagName.toLowerCase(),
                id: e.id || '',
                text: (e.getAttribute('aria-label') || e.textContent || e.value || '').trim().replace(/\s+/g, ' ').slice(0, 80),
                href: e.getAttribute('href'),
                fv: e.matches(':focus-visible'),
                outline: cs.outlineStyle + ' ' + cs.outlineWidth,
                shadow: cs.boxShadow
              });
            }
            """).ConfigureAwait(false);
        var d = JsonDocument.Parse(json).RootElement;
        return new FocusStop(
            d.GetProperty("tag").GetString()!,
            d.GetProperty("id").GetString()!,
            d.GetProperty("text").GetString()!,
            d.GetProperty("href").ValueKind == JsonValueKind.Null ? null : d.GetProperty("href").GetString(),
            d.GetProperty("fv").GetBoolean(),
            d.GetProperty("outline").GetString()!,
            d.GetProperty("shadow").GetString()!);
    }

    /// <summary>Presses Tab <paramref name="count"/> times from the current focus and records every stop.</summary>
    public async Task<IReadOnlyList<FocusStop>> TabAsync(int count)
    {
        var stops = new List<FocusStop>();
        for (var i = 0; i < count; i++)
        {
            await _page.Keyboard.PressAsync("Tab").ConfigureAwait(false);
            stops.Add(await FocusedAsync().ConfigureAwait(false));
        }

        return stops;
    }

    /// <summary>Applies the OS color scheme the shell follows by default and waits for the shell to switch its theme class.</summary>
    public async Task SetSchemeAsync(bool dark)
    {
        await _page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light }).ConfigureAwait(false);
        await _page.WaitForFunctionAsync($"() => document.documentElement.classList.contains('dark') === {(dark ? "true" : "false")}").ConfigureAwait(false);
        // Let color transitions finish so a screenshot or a contrast reading never captures a half-switched theme.
        await _page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')").ConfigureAwait(false);
        await Task.Delay(400).ConfigureAwait(false);
    }

    /// <summary>WCAG contrast ratio of the element's text color against its composited background (solid backgrounds only).</summary>
    public async Task<double> ContrastAsync(ILocator locator)
    {
        return await locator.First.EvaluateAsync<double>("""
            el => {
              const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
              const ctx = canvas.getContext('2d', { willReadFrequently: true });
              const rgba = css => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = '#000'; ctx.fillStyle = css; ctx.fillRect(0, 0, 1, 1); const d = ctx.getImageData(0, 0, 1, 1).data; return [d[0], d[1], d[2], d[3] / 255]; };
              const lin = c => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
              const lum = c => 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2]);
              const chain = []; for (let n = el; n; n = n.parentElement) chain.push(n);
              let base = [255, 255, 255];
              for (const n of chain.reverse()) {
                const c = rgba(getComputedStyle(n).backgroundColor);
                if (c[3] > 0) base = [0, 1, 2].map(i => c[i] * c[3] + base[i] * (1 - c[3]));
              }
              const f = rgba(getComputedStyle(el).color);
              const fg = [0, 1, 2].map(i => f[i] * f[3] + base[i] * (1 - f[3]));
              const a = lum(fg), b = lum(base);
              return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            }
            """).ConfigureAwait(false);
    }

    public async Task ScreenshotAsync(string name, bool dark, ILocator? element = null)
    {
        var folder = Path.Combine(_directory, dark ? "dark" : "light");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        if (element is null)
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true }).ConfigureAwait(false);
        else
            await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path }).ConfigureAwait(false);
        Log("screenshot", Path.GetRelativePath(Path.GetDirectoryName(_directory)!, path));
    }
}
