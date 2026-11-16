namespace Positron;

/// <summary>
/// Configuration for the application's main window, supplied to <see cref="App"/>.<c>Run</c>.
/// </summary>
public sealed class WindowOptions
{
    /// <summary>The window title shown in the title bar.</summary>
    public string Title { get; set; } = "Positron";

    /// <summary>Initial content width in logical pixels.</summary>
    public int Width { get; set; } = 1024;

    /// <summary>Initial content height in logical pixels.</summary>
    public int Height { get; set; } = 768;

    /// <summary>Whether the window can be resized by the user.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// Optional extra JavaScript injected at document-start, after the Positron bridge. Use it
    /// to expose an application-specific preload surface. Null injects only the bridge.
    /// </summary>
    public string? Preload { get; set; }

    /// <summary>
    /// Whether the WebView shows its default (browser) context menu. Defaults to
    /// <see cref="ContextMenuMode.Default"/>. Set to <see cref="ContextMenuMode.Disabled"/> to
    /// suppress it — common for app-like UIs that provide their own right-click handling in JS.
    /// </summary>
    public ContextMenuMode ContextMenu { get; set; } = ContextMenuMode.Default;

    /// <summary>
    /// Whether to enable the WebView's developer tools (right-click → Inspect, where supported).
    /// Defaults to false. Enable during development; leave off in release builds.
    /// </summary>
    public bool EnableDevTools { get; set; }

    /// <summary>
    /// When set, serves this directory over the <c>app://</c> scheme with a real web origin, correct
    /// MIME types, and single-page-application history fallback (unknown routes resolve to
    /// <see cref="ServeEntry"/>). This is the supported way to host a built SPA (Angular, React,
    /// etc.); load it with <c>window.LoadUrl("app://localhost/")</c>. Null disables serving.
    /// </summary>
    public string? ServeDirectory { get; set; }

    /// <summary>
    /// The SPA entry document returned for client-side (history-API) routes under
    /// <see cref="ServeDirectory"/>. Defaults to <c>index.html</c>.
    /// </summary>
    public string ServeEntry { get; set; } = "index.html";
}

/// <summary>Controls the WebView's built-in context menu.</summary>
public enum ContextMenuMode
{
    /// <summary>Show the WebView's default context menu.</summary>
    Default,

    /// <summary>Suppress the default context menu (the page may provide its own).</summary>
    Disabled,
}