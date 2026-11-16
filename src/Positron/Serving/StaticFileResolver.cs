using System.Diagnostics.CodeAnalysis;

namespace Positron.Serving;

/// <summary>
/// Resolves <c>app://</c> request paths to files under a served root directory, with the MIME type
/// and single-page-application history fallback. This is the platform-agnostic core of directory
/// serving; the per-OS scheme handlers (WKURLSchemeHandler, WebView2 virtual host, WebKitGTK URI
/// scheme) call into it. No native dependency, so it is fully unit-testable.
/// </summary>
internal sealed class StaticFileResolver
{
    private readonly string _root;
    private readonly string _entry;

    /// <summary>
    /// Creates a resolver rooted at <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="rootPath">Absolute path to the directory to serve.</param>
    /// <param name="entry">The SPA entry document returned for history-API (non-file) routes.</param>
    public StaticFileResolver(string rootPath, string entry = "index.html")
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentException.ThrowIfNullOrEmpty(entry);
        _root = Path.GetFullPath(rootPath);
        _entry = entry;
    }

    /// <summary>
    /// Resolves a request path (the path component of an <c>app://</c> URL, e.g. <c>/assets/app.js</c>
    /// or <c>/</c>) to a concrete file on disk plus its content type.
    /// </summary>
    /// <param name="requestPath">The URL path, with or without a leading slash.</param>
    /// <param name="filePath">The resolved absolute file path, if found.</param>
    /// <param name="contentType">The MIME type for the resolved file.</param>
    /// <returns>True if a file was resolved (directly or via SPA fallback); false if nothing matched.</returns>
    public bool TryResolve(string requestPath, [NotNullWhen(true)] out string? filePath, out string contentType)
    {
        filePath = null;
        contentType = "application/octet-stream";

        string relative = NormalizeRelative(requestPath);

        // A request for the root, or a path with no file extension (a client-side route), serves the
        // SPA entry document so history-API routing works.
        if (relative.Length == 0 || !Path.HasExtension(relative))
        {
            string entryPath = Path.Combine(_root, _entry);
            if (File.Exists(entryPath))
            {
                filePath = entryPath;
                contentType = GetContentType(entryPath);
                return true;
            }

            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_root, relative));

        // Containment check: never serve outside the root (path-traversal guard).
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(candidate, _root, StringComparison.Ordinal))
        {
            return false;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        filePath = candidate;
        contentType = GetContentType(candidate);
        return true;
    }

    /// <summary>Strips a leading slash and normalizes to a relative path; rejects traversal segments.</summary>
    private static string NormalizeRelative(string requestPath)
    {
        string p = requestPath ?? string.Empty;
        int query = p.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            p = p[..query];
        }

        p = p.TrimStart('/');
        return p.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>Maps a file extension to a content type for the common web asset set.</summary>
    public static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".wasm" => "application/wasm",
            ".map" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
}