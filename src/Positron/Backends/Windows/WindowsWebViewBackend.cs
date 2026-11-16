using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#if POSITRON_WEBVIEW2
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
#endif

namespace Positron.Backends.Windows;

/// <summary>
/// Windows WebView2 backend hosting the Edge/Chromium WebView2 control on a bare Win32 window
/// (no WinForms/WPF), driven through the managed <c>Microsoft.Web.WebView2.Core</c> API. Implements
/// the window host and the IPC transport: JS→C# arrives via <c>CoreWebView2.WebMessageReceived</c>
/// (<c>window.chrome.webview.postMessage</c>); C#→JS goes out via <c>ExecuteScriptAsync</c>.
/// </summary>
/// <remarks>
/// The WebView2-dependent implementation compiles only when the <c>POSITRON_WEBVIEW2</c> constant is
/// defined (i.e. the <c>Microsoft.Web.WebView2</c> package is referenced, which happens on a Windows
/// build host). On non-Windows build hosts the type still exists for <see cref="Backends.BackendFactory"/>
/// but its members throw — they are never reached because the factory only constructs it on Windows.
/// Design-complete but unverified on the development machine (macOS). Requires the WebView2 Evergreen
/// Runtime to be installed on the target machine.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWebViewBackend : IWebViewBackend
{
    // CS0067: on a non-Windows build host the WebView2 branch is excluded and nothing raises this
    // event; the handler is wired only in the POSITRON_WEBVIEW2 branch.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event Action<string>? MessageReceived;
#pragma warning restore CS0067

#if POSITRON_WEBVIEW2
    private const string WindowClassName = "PositronWindow";

    private IntPtr _hwnd;
    private IntPtr _classNamePtr;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private string _pendingPreload = string.Empty;
    private WindowOptions? _options;
    private string? _pendingHtml;
    private string? _pendingUrl;
    private readonly Queue<string> _pendingEval = new();

    // Keep the WndProc delegate alive for the window's lifetime.
    private Win32WndProc? _wndProc;
    private delegate IntPtr Win32WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <inheritdoc />
    public void Initialize(WindowOptions options, string preloadScript)
    {
        _pendingPreload = preloadScript;
        _options = options;
        IntPtr hInstance = Win32.GetModuleHandle(IntPtr.Zero);
        _wndProc = WndProc;

        // Pin the class name for the lifetime of the window (the OS keeps a pointer to it).
        _classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

        var wndClass = new Win32.WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<Win32.WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = hInstance,
            ClassName = _classNamePtr,
        };
        Win32.RegisterClassEx(ref wndClass);

        _hwnd = Win32.CreateWindowEx(
            0, WindowClassName, options.Title, Win32.WsOverlappedWindow,
            Win32.CwUseDefault, Win32.CwUseDefault, options.Width, options.Height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // WebView2 environment + controller creation is async; kick it off and wire up on completion.
        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync();
        _controller = await environment.CreateCoreWebView2ControllerAsync(_hwnd);
        _webView = _controller.CoreWebView2;

        ResizeToClient();
        _controller.IsVisible = true;

        // Transport shim + bridge, injected before any page script runs. On WebView2 the inbound
        // path is window.chrome.webview.postMessage, which is always present — the shim adapts it to
        // the backend-agnostic window.__positronPostToNative the bridge calls.
        string shim =
            "window.__positronPostToNative = function (json) {"
            + " window.chrome.webview.postMessage(json); };";
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(shim);
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(_pendingPreload);

        _webView.WebMessageReceived += (_, e) => MessageReceived?.Invoke(e.TryGetWebMessageAsString());

        // Settings: dev tools and context menu (WebView2 exposes these as settings rather than a
        // native delegate). app:// serving maps to a virtual host backed by the served folder.
        if (_options is not null)
        {
            _webView.Settings.AreDevToolsEnabled = _options.EnableDevTools;
            _webView.Settings.AreDefaultContextMenusEnabled = _options.ContextMenu == ContextMenuMode.Default;

            if (_options.ServeDirectory is not null)
            {
                _webView.SetVirtualHostNameToFolderMapping(
                    "localhost", _options.ServeDirectory,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }
        }

        if (_pendingHtml is not null)
        {
            _webView.NavigateToString(_pendingHtml);
        }
        else if (_pendingUrl is not null)
        {
            _webView.Navigate(_pendingUrl);
        }

        // Flush any Evaluate/PostMessage calls that arrived before the WebView finished async init.
        while (_pendingEval.Count > 0)
        {
            _ = _webView.ExecuteScriptAsync(_pendingEval.Dequeue());
        }
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        if (_webView is null)
        {
            _pendingUrl = url;
            return;
        }

        _webView.Navigate(url);
    }

    /// <inheritdoc />
    public void LoadHtml(string html)
    {
        if (_webView is null)
        {
            _pendingHtml = html;
            return;
        }

        _webView.NavigateToString(html);
    }

    /// <inheritdoc />
    public void Evaluate(string javaScript)
    {
        if (_webView is null)
        {
            // Queue until async init completes, matching Navigate/LoadHtml rather than dropping.
            _pendingEval.Enqueue(javaScript);
            return;
        }

        _ = _webView.ExecuteScriptAsync(javaScript);
    }

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(json, WindowsJsonContext.Default.String);
        Evaluate($"window.__positronReceive({escaped});");
    }

    /// <inheritdoc />
    public void Run()
    {
        Win32.ShowWindow(_hwnd, Win32.SwShow);
        while (Win32.GetMessage(out Win32.Msg msg, IntPtr.Zero, 0, 0))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
        }
    }

    /// <inheritdoc />
    public Task<string?> ShowOpenDialogAsync(Positron.Files.OpenFileOptions options) =>
        Task.FromResult(Win32Dialogs.ShowOpen(_hwnd, options));

    /// <inheritdoc />
    public Task<string?> ShowSaveDialogAsync(Positron.Files.SaveFileOptions options) =>
        Task.FromResult(Win32Dialogs.ShowSave(_hwnd, options));

    /// <inheritdoc />
    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
        _webView = null;
        if (_classNamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_classNamePtr);
            _classNamePtr = IntPtr.Zero;
        }
    }

    private void ResizeToClient()
    {
        if (_controller is null || !Win32.GetClientRect(_hwnd, out Win32.Rect r))
        {
            return;
        }

        _controller.Bounds = new System.Drawing.Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WmSize:
                ResizeToClient();
                return IntPtr.Zero;
            case Win32.WmDestroy:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
#else
    private const string NotBuilt =
        "The Positron Windows backend was not compiled: the Microsoft.Web.WebView2 package is only "
        + "referenced on a Windows build host (POSITRON_WEBVIEW2). Build on Windows to enable it.";

    /// <inheritdoc />
    public void Initialize(WindowOptions options, string preloadScript) => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void Navigate(string url) => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void LoadHtml(string html) => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void Evaluate(string javaScript) => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void PostMessage(string json) => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void Run() => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void Close() => throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public Task<string?> ShowOpenDialogAsync(Positron.Files.OpenFileOptions options) =>
        throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public Task<string?> ShowSaveDialogAsync(Positron.Files.SaveFileOptions options) =>
        throw new PlatformNotSupportedException(NotBuilt);

    /// <inheritdoc />
    public void Dispose()
    {
    }
#endif
}

/// <summary>Source-generated context for the one string the Windows backend serializes.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class WindowsJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}