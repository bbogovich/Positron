using Positron.Files;
using Positron.Ipc;

namespace Positron.Backends;

/// <summary>
/// The per-platform native WebView façade. Each OS backend (WKWebView on macOS, WebView2 on
/// Windows, WebKitGTK on Linux) implements this interface; <see cref="BackendFactory"/> selects
/// the right one at runtime. The public <see cref="App"/>/<see cref="Window"/> types and the
/// <see cref="IpcRouter"/> are written entirely against this abstraction.
/// </summary>
/// <remarks>
/// A backend both hosts the window and acts as the <see cref="IMessageTransport"/> for the IPC
/// router — it owns the native message bridge in both directions.
/// <para>
/// <b>Threading.</b> <see cref="Initialize"/> and <see cref="Run"/> are called on the UI (main)
/// thread. Every other member may be called from any thread — async handler continuations and
/// timers run on the thread pool — and the backend marshals the native work onto the UI thread.
/// </para>
/// <para>
/// <b>Trust.</b> A backend delivers inbound messages only from, and posts outbound messages only to,
/// a main-frame document whose origin the host loaded (see <see cref="TrustedOrigins"/>).
/// </para>
/// </remarks>
internal interface IWebViewBackend : IMessageTransport, IDisposable
{
    /// <summary>
    /// Raised once, on the UI thread, when the native window closes — whether the user closed it or
    /// <see cref="Close"/> was called. The run loop exits after it, so <see cref="Run"/> returns.
    /// </summary>
    event Action? Closed;

    /// <summary>
    /// Creates the native window and WebView, injects the preload bridge script at document
    /// start, and wires the native message handler. Must be called before <see cref="Run"/>.
    /// </summary>
    /// <param name="options">Window configuration.</param>
    /// <param name="preloadScript">
    /// The JavaScript to inject before any page script runs (the Positron bridge plus any
    /// application preload). Injected at document-start on the main frame.
    /// </param>
    void Initialize(WindowOptions options, string preloadScript);

    /// <summary>Navigates the WebView to a URL (e.g. <c>file://</c> or <c>http://</c>).</summary>
    void Navigate(string url);

    /// <summary>Loads an HTML string directly into the WebView.</summary>
    void LoadHtml(string html);

    /// <summary>
    /// Evaluates JavaScript in the WebView. Used by the IPC transport to deliver inbound
    /// messages; the result, if any, is discarded.
    /// </summary>
    void Evaluate(string javaScript);

    /// <summary>
    /// Enters the native run loop and blocks until the window closes. Must be called on the
    /// process main thread on macOS; on an STA thread with a message pump on Windows.
    /// </summary>
    void Run();

    /// <summary>
    /// Requests that the native run loop exit, unblocking <see cref="Run"/>. Safe to call from any
    /// thread; the backend marshals to its UI thread as needed.
    /// </summary>
    void Close();

    /// <summary>
    /// Shows a native open-file dialog and returns the chosen path, or null if cancelled.
    /// </summary>
    Task<string?> ShowOpenDialogAsync(OpenFileOptions options);

    /// <summary>
    /// Shows a native save-file dialog and returns the chosen path, or null if cancelled.
    /// </summary>
    Task<string?> ShowSaveDialogAsync(SaveFileOptions options);
}