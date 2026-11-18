using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Positron.Interop;

using static Positron.Backends.Linux.WebKitGtkInterop;

namespace Positron.Backends.Linux;

/// <summary>
/// Linux WebView backend hosting a <c>WebKitWebView</c> in a GTK3 top-level window, driven through
/// GObject P/Invoke against <c>webkit2gtk-4.1</c>. Implements the window host and the IPC transport:
/// JS→C# arrives via a registered <c>script-message-received</c> signal handler; C#→JS goes out via
/// <c>webkit_web_view_evaluate_javascript</c>.
/// </summary>
/// <remarks>
/// Design-complete but unverified on the development machine (macOS). Mirrors the structure and the
/// verified contract of the macOS backend: a transport shim defines <c>window.__positronPostToNative</c>
/// on top of <c>window.webkit.messageHandlers.positron.postMessage</c>, injected before the bridge.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LinuxWebViewBackend : IWebViewBackend
{
    private const string MessageHandlerName = "positron";

    private IntPtr _window;
    private IntPtr _webView;
    private IntPtr _contentManager;
    private ulong _signalHandlerId;
    private int _uiThreadId;
    private int _closedRaised;
    private readonly TrustedOrigins _trust = new();
    private NativeCallbackContext<LinuxWebViewBackend>? _callbackContext;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <inheritdoc />
    public event Action? Closed;

    /// <inheritdoc />
    public void Initialize(WindowOptions options, string preloadScript)
    {
        gtk_init(IntPtr.Zero, IntPtr.Zero);
        _uiThreadId = Environment.CurrentManagedThreadId;

        if (options.ServeDirectory is not null)
        {
            // app:// serving is not yet implemented on this backend; trusting the origin keeps the
            // bridge working once it is.
            _trust.Trust("app://localhost/");
        }

        _window = gtk_window_new(GtkWindowToplevel);
        gtk_window_set_title(_window, options.Title);
        gtk_window_set_default_size(_window, options.Width, options.Height);
        gtk_window_set_resizable(_window, options.Resizable);

        _webView = webkit_web_view_new();
        _contentManager = webkit_web_view_get_user_content_manager(_webView);

        // Register the message handler name → window.webkit.messageHandlers.positron, and connect the
        // script-message-received::positron signal to the static IMP, keyed by this backend. The
        // handler id is retained so Dispose can g_signal_handler_disconnect it (lifetime parity with
        // the macOS backend); the NativeCallbackContext stays alive via _callbackContext until then.
        _callbackContext = new NativeCallbackContext<LinuxWebViewBackend>(this);
        webkit_user_content_manager_register_script_message_handler(_contentManager, MessageHandlerName);

        unsafe
        {
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> handler = &OnScriptMessage;
            _signalHandlerId = g_signal_connect_data(_contentManager, "script-message-received::" + MessageHandlerName,
                (IntPtr)handler, _callbackContext.Token, IntPtr.Zero, connectFlags: 0);

            // "destroy" fires however the window goes away (title-bar close, window manager,
            // Close()); without it gtk_main() keeps running after the user closes the window.
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> onDestroy = &OnWindowDestroy;
            g_signal_connect_data(_window, "destroy", (IntPtr)onDestroy, _callbackContext.Token, IntPtr.Zero, connectFlags: 0);
        }

        // Inject the transport shim then the bridge at document start.
        string shim =
            "window.__positronPostToNative = function (json) {"
            + " window.webkit.messageHandlers." + MessageHandlerName + ".postMessage(json); };";
        AddUserScript(_contentManager, shim);
        AddUserScript(_contentManager, preloadScript);

        gtk_container_add(_window, _webView);
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        _trust.Trust(url);
        RunOnUi(() => webkit_web_view_load_uri(_webView, url));
    }

    /// <inheritdoc />
    public void LoadHtml(string html)
    {
        _trust.Trust("about:blank");
        RunOnUi(() => webkit_web_view_load_html(_webView, html, baseUri: "about:blank"));
    }

    /// <inheritdoc />
    public void Evaluate(string javaScript) =>
        RunOnUi(() => webkit_web_view_evaluate_javascript(
            _webView, javaScript, -1, null, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(json, LinuxJsonContext.Default.String);
        RunOnUi(() =>
        {
            // Never deliver results or events to a document the host did not load.
            if (!_trust.IsTrusted(CurrentUri()))
            {
                return;
            }

            webkit_web_view_evaluate_javascript(_webView, $"window.__positronReceive({escaped});", -1, null, null,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        });
    }

    /// <inheritdoc />
    public void Run()
    {
        gtk_widget_show_all(_window);
        gtk_main();
    }

    /// <inheritdoc />
    public void Close() => RunOnUi(() =>
    {
        // Destroying the window emits "destroy" → OnWindowDestroy raises Closed and quits gtk_main.
        if (_window != IntPtr.Zero)
        {
            gtk_widget_destroy(_window);
        }
        else
        {
            gtk_main_quit();
        }
    });

    /// <inheritdoc />
    public Task<string?> ShowOpenDialogAsync(Positron.Files.OpenFileOptions options) =>
        InvokeOnUiAsync(() => RunFileChooser(options.Title ?? "Open", save: false, suggestedName: null));

    /// <inheritdoc />
    public Task<string?> ShowSaveDialogAsync(Positron.Files.SaveFileOptions options) =>
        InvokeOnUiAsync(() => RunFileChooser(options.Title ?? "Save", save: true, suggestedName: options.SuggestedFileName));

    /// <summary>The main frame's current URI, or null. GTK thread only.</summary>
    private string? CurrentUri()
    {
        IntPtr uri = webkit_web_view_get_uri(_webView);
        return uri == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(uri);
    }

    /// <summary>
    /// Runs <paramref name="action"/> now on the GTK thread, or schedules it there with
    /// <c>g_idle_add</c> when called from another thread. GTK and WebKitGTK are not thread-safe.
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return;
        }

        GCHandle handle = GCHandle.Alloc(action);
        unsafe
        {
            g_idle_add(&OnIdle, GCHandle.ToIntPtr(handle));
        }
    }

    private Task<T> InvokeOnUiAsync<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnIdle(IntPtr data)
    {
        GCHandle handle = GCHandle.FromIntPtr(data);
        try
        {
            (handle.Target as Action)?.Invoke();
        }
        catch (Exception ex)
        {
            // An exception escaping a native callback terminates the process; report it instead.
            Console.Error.WriteLine($"positron: UI-thread callback failed: {ex}");
        }
        finally
        {
            handle.Free();
        }

        return 0; // G_SOURCE_REMOVE: run once.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWindowDestroy(IntPtr widget, IntPtr userData)
    {
        LinuxWebViewBackend? backend = NativeCallbackContext<LinuxWebViewBackend>.Resolve(userData);
        if (backend is null)
        {
            return;
        }

        backend._window = IntPtr.Zero; // destroyed: must not be touched again
        if (Interlocked.Exchange(ref backend._closedRaised, 1) == 0)
        {
            try
            {
                backend.Closed?.Invoke();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"positron: Closed handler threw: {ex}");
            }
        }

        gtk_main_quit();
    }

    // GtkFileChooserNative action: 0 = open, 1 = save. Response ACCEPT = -3.
    private string? RunFileChooser(string title, bool save, string? suggestedName)
    {
        IntPtr dialog = gtk_file_chooser_native_new(title, _window, save ? 1 : 0, null, null);
        if (save && !string.IsNullOrEmpty(suggestedName))
        {
            gtk_file_chooser_set_current_name(dialog, suggestedName);
        }

        int response = gtk_native_dialog_run(dialog);
        string? path = null;
        if (response == GtkResponseAccept)
        {
            IntPtr raw = gtk_file_chooser_get_filename(dialog);
            path = WebKitGtkInterop.TakeGString(raw);
        }

        g_object_unref(dialog);
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Disconnect the signal before freeing the callback token so GObject cannot invoke the
        // handler with a dangling user-data pointer (lifetime parity with the macOS backend).
        if (_signalHandlerId != 0 && _contentManager != IntPtr.Zero)
        {
            g_signal_handler_disconnect(_contentManager, _signalHandlerId);
            _signalHandlerId = 0;
        }

        _callbackContext?.Dispose();
        _callbackContext = null;
    }

    private static void AddUserScript(IntPtr manager, string source)
    {
        // Top frame only (parity with macOS): iframes do not get the bridge.
        IntPtr script = webkit_user_script_new(source, WebKitUserContentInjectTopFrame,
            WebKitUserScriptInjectAtDocumentStart, IntPtr.Zero, IntPtr.Zero);
        webkit_user_content_manager_add_script(manager, script);
    }

    /// <summary>
    /// The native signal handler for <c>script-message-received::positron</c>. On webkit2gtk-4.1 the
    /// second argument is a <c>WebKitJavascriptResult*</c>; unwrap it to a <c>JSCValue*</c> and read
    /// the JSON string. Static and blittable-only per <c>[UnmanagedCallersOnly]</c>; resolves the
    /// owning backend from the user-data token.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnScriptMessage(IntPtr userContentManager, IntPtr javascriptResult, IntPtr userData)
    {
        LinuxWebViewBackend? backend = NativeCallbackContext<LinuxWebViewBackend>.Resolve(userData);
        if (backend is null)
        {
            return;
        }

        // Only documents the host loaded may use the bridge. This signal carries no frame info in
        // webkit2gtk-4.1, so the check is on the main frame's URI.
        if (!backend._trust.IsTrusted(backend.CurrentUri()))
        {
            return;
        }

        IntPtr jsValue = webkit_javascript_result_get_js_value(javascriptResult);
        string json = TakeGString(jsc_value_to_string(jsValue));
        backend.MessageReceived?.Invoke(json);
    }
}

/// <summary>Source-generated context for the one string the Linux backend serializes.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class LinuxJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}