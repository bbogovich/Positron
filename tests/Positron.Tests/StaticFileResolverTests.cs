using Positron.Serving;

namespace Positron.Tests;

/// <summary>
/// Unit tests for <see cref="StaticFileResolver"/> — the platform-agnostic core of <c>app://</c>
/// directory serving (MIME typing, SPA history fallback, path-traversal containment). Uses a temp
/// directory so no native WebView is involved.
/// </summary>
public sealed class StaticFileResolverTests : IDisposable
{
    private readonly string _root;

    public StaticFileResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "positron-serve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><title>root</title>");
        File.WriteAllText(Path.Combine(_root, "assets", "app.js"), "console.log(1)");
        File.WriteAllText(Path.Combine(_root, "assets", "styles.css"), "body{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private StaticFileResolver Create() => new(_root);

    [Fact]
    public void ResolvesAnExistingAssetWithCorrectMime()
    {
        bool ok = Create().TryResolve("/assets/app.js", out string? path, out string contentType);
        Assert.True(ok);
        Assert.Equal(Path.Combine(_root, "assets", "app.js"), path);
        Assert.StartsWith("text/javascript", contentType);
    }

    [Fact]
    public void ResolvesCssMime()
    {
        Create().TryResolve("/assets/styles.css", out _, out string contentType);
        Assert.StartsWith("text/css", contentType);
    }

    [Fact]
    public void RootPathServesEntryDocument()
    {
        bool ok = Create().TryResolve("/", out string? path, out string contentType);
        Assert.True(ok);
        Assert.Equal(Path.Combine(_root, "index.html"), path);
        Assert.StartsWith("text/html", contentType);
    }

    [Fact]
    public void ClientSideRouteFallsBackToEntry()
    {
        // A history-API route (no file extension) resolves to the SPA entry, not a 404.
        bool ok = Create().TryResolve("/users/42/profile", out string? path, out _);
        Assert.True(ok);
        Assert.Equal(Path.Combine(_root, "index.html"), path);
    }

    [Fact]
    public void MissingAssetWithExtensionIsNotFound()
    {
        bool ok = Create().TryResolve("/assets/missing.js", out string? path, out _);
        Assert.False(ok);
        Assert.Null(path);
    }

    [Fact]
    public void StripsQueryStringBeforeResolving()
    {
        bool ok = Create().TryResolve("/assets/app.js?v=123", out string? path, out _);
        Assert.True(ok);
        Assert.Equal(Path.Combine(_root, "assets", "app.js"), path);
    }

    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/assets/../../secret.txt")]
    public void RejectsPathTraversal(string requestPath)
    {
        // Seed a file OUTSIDE the root that traversal would reach.
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, "secret.txt");
        File.WriteAllText(outside, "secret");
        try
        {
            bool ok = Create().TryResolve(requestPath, out string? path, out _);
            Assert.False(ok);
            Assert.Null(path);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("a.html", "text/html; charset=utf-8")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.wasm", "application/wasm")]
    [InlineData("a.woff2", "font/woff2")]
    [InlineData("a.unknown", "application/octet-stream")]
    public void GetContentType_MapsCommonExtensions(string file, string expected) =>
        Assert.Equal(expected, StaticFileResolver.GetContentType(file));

    [Fact]
    public void Ctor_EmptyRoot_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => new StaticFileResolver(""));
}