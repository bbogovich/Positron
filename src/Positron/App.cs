using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Positron.Backends;
using Positron.Ipc;

namespace Positron;

/// <summary>
/// The entry point for a Positron application. <see cref="Run(WindowOptions, Action{Window}, JsonSerializerOptions)"/>
/// creates the native window and WebView for the host OS, wires the IPC bridge, lets the caller
/// register handlers and load content, then enters the native run loop.
/// </summary>
public static class App
{
    /// <summary>
    /// Runs a Positron application: builds the window, exposes <c>window.positron</c> to the web
    /// content, invokes <paramref name="configure"/> so the caller can register IPC handlers and
    /// load content, then blocks in the native run loop until the window closes.
    /// </summary>
    /// <param name="options">Window configuration.</param>
    /// <param name="configure">
    /// Callback invoked with the ready <see cref="Window"/> before the run loop starts. Register
    /// handlers (<see cref="Window.Handle{TArg, TResult}(string, Func{TArg, TResult})"/>) and load
    /// content (<see cref="Window.LoadUrl(string)"/> / <see cref="Window.LoadHtml(string)"/>) here.
    /// </param>
    /// <param name="serializerOptions">
    /// JSON options for IPC handler argument and result types. Pass a source-generated context's
    /// <c>Options</c> property (e.g. <c>MyJsonContext.Default.Options</c>); this keeps the app
    /// trim- and NativeAOT-safe, which is the point of Positron's small-footprint model. It is
    /// required precisely so an AOT build cannot silently fall back to reflection-based
    /// serialization and break at runtime.
    /// </param>
    /// <remarks>
    /// Must be called from the process main thread. On macOS AppKit requires it; on Windows the
    /// backend establishes an STA message pump on the calling thread.
    /// </remarks>
    public static void Run(WindowOptions options, Action<Window> configure, JsonSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(serializerOptions);

        using IWebViewBackend backend = BackendFactory.Create();
        RunWith(backend, options, configure, serializerOptions);
    }

    /// <summary>
    /// The body of <see cref="Run"/> against an explicit backend, so the host wiring can be tested
    /// with a fake backend. The caller owns (and disposes) <paramref name="backend"/>.
    /// </summary>
    internal static void RunWith(
        IWebViewBackend backend, WindowOptions options, Action<Window> configure, JsonSerializerOptions serializerOptions)
    {
        string preload = BuildPreload(options.Preload);
        backend.Initialize(options, preload);

        // Combine the caller's resolver with the library's built-in-channel context so both the
        // app's types and the reserved positron.* payloads serialize under trimming/NativeAOT.
        var combined = new JsonSerializerOptions(serializerOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                serializerOptions.TypeInfoResolver,
                PositronBuiltinsContext.Default),
        };

        // The router reports handler faults through the window, which is created just below; no
        // message can arrive before the run loop starts, so the window is always set by then.
        Window? window = null;
        using var router = new IpcRouter(backend, combined,
            onHandlerError: (channel, exception) => window?.ReportHandlerError(channel, exception));
        window = new Window(backend, router);
        window.RegisterBuiltins(backend);

        configure(window);

        backend.Run();
    }

    /// <summary>
    /// Composes the document-start preload: the Positron bridge, optionally followed by an
    /// application-supplied preload. Internal so it can be unit-tested directly.
    /// </summary>
    internal static string BuildPreload(string? appPreload) =>
        appPreload is null
            ? BridgeScript.Source
            : BridgeScript.Source + "\n" + appPreload;
}