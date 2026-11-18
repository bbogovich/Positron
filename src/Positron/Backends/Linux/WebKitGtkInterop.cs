using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Positron.Backends.Linux;

/// <summary>
/// P/Invoke bindings for GTK3, WebKitGTK (<c>webkit2gtk-4.1</c>), GLib/GObject, and JavaScriptCore
/// used by the Linux backend. Bound by soname so the runtime linker resolves the installed library.
/// </summary>
/// <remarks>
/// Target baseline is <c>libwebkit2gtk-4.1</c> / GTK3 — the configuration Tauri/wry ship and the
/// one with the widest distro coverage. The newer <c>webkitgtk-6.0</c>/GTK4 build changes the
/// <c>script-message-received</c> callback's second argument from <c>WebKitJavascriptResult*</c> to
/// a bare <c>JSCValue*</c>; this binding handles the 4.1 shape. (A 6.0 fallback would probe the
/// alternate soname and branch the callback.)
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class WebKitGtkInterop
{
    private const string Gtk = "libgtk-3.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Jsc = "libjavascriptcoregtk-4.1.so.0";

    // ── GTK ─────────────────────────────────────────────────────────────────────────────────
    public const int GtkWindowToplevel = 0;

    [LibraryImport(Gtk)]
    public static partial void gtk_init(IntPtr argc, IntPtr argv);

    [LibraryImport(Gtk)]
    public static partial IntPtr gtk_window_new(int type);

    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(IntPtr window, string title);

    [LibraryImport(Gtk)]
    public static partial void gtk_window_set_default_size(IntPtr window, int width, int height);

    [LibraryImport(Gtk)]
    public static partial void gtk_window_set_resizable(IntPtr window, [MarshalAs(UnmanagedType.I4)] bool resizable);

    [LibraryImport(Gtk)]
    public static partial void gtk_container_add(IntPtr container, IntPtr widget);

    // ── GTK native file chooser ───────────────────────────────────────────────────────────────
    public const int GtkResponseAccept = -3;

    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr gtk_file_chooser_native_new(
        string title, IntPtr parent, int action, string? acceptLabel, string? cancelLabel);

    [LibraryImport(Gtk)]
    public static partial int gtk_native_dialog_run(IntPtr dialog);

    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_chooser_set_current_name(IntPtr chooser, string name);

    [LibraryImport(Gtk)]
    public static partial IntPtr gtk_file_chooser_get_filename(IntPtr chooser);

    [LibraryImport(GObject)]
    public static partial void g_object_unref(IntPtr obj);

    [LibraryImport(Gtk)]
    public static partial void gtk_widget_show_all(IntPtr widget);

    [LibraryImport(Gtk)]
    public static partial void gtk_main();

    [LibraryImport(Gtk)]
    public static partial void gtk_main_quit();

    [LibraryImport(Gtk)]
    public static partial void gtk_widget_destroy(IntPtr widget);

    // ── WebKitGTK ───────────────────────────────────────────────────────────────────────────
    public const int WebKitUserContentInjectAllFrames = 0;
    public const int WebKitUserContentInjectTopFrame = 1;
    public const int WebKitUserScriptInjectAtDocumentStart = 0;

    [LibraryImport(WebKit)]
    public static partial IntPtr webkit_web_view_new();

    [LibraryImport(WebKit)]
    public static partial IntPtr webkit_web_view_get_user_content_manager(IntPtr webView);

    /// <summary>The main frame's current URI. The returned string is owned by the web view — do not free.</summary>
    [LibraryImport(WebKit)]
    public static partial IntPtr webkit_web_view_get_uri(IntPtr webView);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(IntPtr webView, string uri);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_html(IntPtr webView, string content, string? baseUri);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_evaluate_javascript(
        IntPtr webView, string script, nint length, string? worldName, string? sourceUri,
        IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool webkit_user_content_manager_register_script_message_handler(
        IntPtr manager, string name);

    [LibraryImport(WebKit)]
    public static partial void webkit_user_content_manager_add_script(IntPtr manager, IntPtr script);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr webkit_user_script_new(
        string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);

    [LibraryImport(WebKit)]
    public static partial IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

    // ── JavaScriptCore ──────────────────────────────────────────────────────────────────────
    [LibraryImport(Jsc)]
    public static partial IntPtr jsc_value_to_string(IntPtr value);

    // ── GObject signals ─────────────────────────────────────────────────────────────────────
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong g_signal_connect_data(
        IntPtr instance, string detailedSignal, IntPtr handler, IntPtr data, IntPtr destroyData, int connectFlags);

    [LibraryImport(GObject)]
    public static partial void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    // ── GLib main loop ──────────────────────────────────────────────────────────────────────
    /// <summary>Schedules <paramref name="function"/> on the default main context (the GTK thread).</summary>
    [LibraryImport(GLib)]
    public static unsafe partial uint g_idle_add(delegate* unmanaged[Cdecl]<IntPtr, int> function, IntPtr data);

    // ── GLib memory ─────────────────────────────────────────────────────────────────────────
    [LibraryImport(GLib)]
    public static partial void g_free(IntPtr mem);

    /// <summary>Reads and frees a GLib-allocated UTF-8 C string into a managed string.</summary>
    public static string TakeGString(IntPtr utf8)
    {
        if (utf8 == IntPtr.Zero)
        {
            return string.Empty;
        }

        string result = Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
        g_free(utf8);
        return result;
    }
}