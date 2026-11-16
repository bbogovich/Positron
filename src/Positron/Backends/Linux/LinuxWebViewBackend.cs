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
    private NativeCallbackContext<LinuxWebViewBackend>? _callbackContext;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <inheritdoc />
    public void Initialize(WindowOptions options, string preloadScript)
    {
        gtk_init(IntPtr.Zero, IntPtr.Zero);

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
    public void Navigate(string url) => webkit_web_view_load_uri(_webView, url);

    /// <inheritdoc />
    public void LoadHtml(string html) => webkit_web_view_load_html(_webView, html, baseUri: "about:blank");

    /// <inheritdoc />
    public void Evaluate(string javaScript) =>
        webkit_web_view_evaluate_javascript(_webView, javaScript, -1, null, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(json, LinuxJsonContext.Default.String);
        Evaluate($"window.__positronReceive({escaped});");
    }

    /// <inheritdoc />
    public void Run()
    {
        gtk_widget_show_all(_window);
        gtk_main();
    }

    /// <inheritdoc />
    public void Close() => gtk_main_quit();

    /// <inheritdoc />
    public Task<string?> ShowOpenDialogAsync(Positron.Files.OpenFileOptions options) =>
        Task.FromResult(RunFileChooser(options.Title ?? "Open", save: false, suggestedName: null));

    /// <inheritdoc />
    public Task<string?> ShowSaveDialogAsync(Positron.Files.SaveFileOptions options) =>
        Task.FromResult(RunFileChooser(options.Title ?? "Save", save: true, suggestedName: options.SuggestedFileName));

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
        IntPtr script = webkit_user_script_new(source, WebKitUserContentInjectAllFrames,
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