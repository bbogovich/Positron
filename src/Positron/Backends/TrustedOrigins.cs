using System.Collections.Concurrent;

namespace Positron.Backends;

/// <summary>
/// The set of web origins the host itself loaded, and therefore trusts to use the IPC bridge.
/// </summary>
/// <remarks>
/// <para>
/// The preload bridge (and, on WebKit, <c>window.webkit.messageHandlers</c>) is reachable from
/// whatever document the WebView's main frame is showing. Without a gate, a page from another origin
/// — reached by the user clicking a link, or by a redirect — could call every registered channel,
/// including the built-in <c>positron.fs.*</c> file I/O. Backends therefore consult this set:
/// inbound messages from an untrusted document are dropped, and outbound results/events are not
/// delivered to one.
/// </para>
/// <para>
/// An origin becomes trusted only when the host loads it: <see cref="Window.LoadUrl"/> trusts the
/// URL's origin, <see cref="Window.LoadHtml"/> trusts <c>about:blank</c> (the base URL HTML strings
/// are loaded with), and <see cref="WindowOptions.ServeDirectory"/> trusts <c>app://localhost</c>.
/// Thread-safe.
/// </para>
/// </remarks>
internal sealed class TrustedOrigins
{
    private readonly ConcurrentDictionary<string, byte> _origins = new(StringComparer.Ordinal);

    /// <summary>Trusts the origin of <paramref name="url"/>. URLs without a usable origin are ignored.</summary>
    /// <param name="url">An absolute URL the host is about to load.</param>
    public void Trust(string url)
    {
        string? origin = OriginOf(url);
        if (origin is not null)
        {
            _origins.TryAdd(origin, 0);
        }
    }

    /// <summary>Whether the document at <paramref name="url"/> belongs to a trusted origin.</summary>
    /// <param name="url">The URL of the document sending or receiving a message; null if unknown.</param>
    /// <returns>True only if the URL has an origin and the host trusted it.</returns>
    public bool IsTrusted(string? url)
    {
        string? origin = OriginOf(url);
        return origin is not null && _origins.ContainsKey(origin);
    }

    /// <summary>
    /// Computes a comparable origin string for <paramref name="url"/>: <c>scheme://host[:port]</c>
    /// (lowercase, default ports omitted), <c>file://</c> for any local file, <c>about:blank</c>
    /// for HTML loaded from a string, and null for URLs with no trustworthy origin (relative URLs,
    /// <c>data:</c>, <c>blob:</c>, <c>javascript:</c>, other <c>about:</c> pages, host-less URLs).
    /// </summary>
    /// <param name="url">The URL to classify.</param>
    /// <returns>The origin, or null.</returns>
    internal static string? OriginOf(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        // On Unix, Uri parses a bare path such as "/x" as an absolute file URI. Require the URL to
        // spell out its scheme so only real URLs (what WebViews report) are classified.
        if (!url.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string scheme = uri.Scheme.ToLowerInvariant();
        switch (scheme)
        {
            case "about":
                return string.Equals(uri.AbsolutePath, "blank", StringComparison.OrdinalIgnoreCase) ? "about:blank" : null;
            case "file":
                // Local files share one origin: the threat this guards against is remote content.
                return "file://";
            case "data":
            case "blob":
            case "javascript":
                return null;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        string host = uri.Host.ToLowerInvariant();
        return uri.IsDefaultPort || uri.Port < 0
            ? $"{scheme}://{host}"
            : $"{scheme}://{host}:{uri.Port}";
    }
}