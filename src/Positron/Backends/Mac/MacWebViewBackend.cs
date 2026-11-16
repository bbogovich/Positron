using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Positron.Interop;

using static Positron.Backends.Mac.ObjC;

namespace Positron.Backends.Mac;

/// <summary>
/// macOS WebView backend hosting a <c>WKWebView</c> in an <c>NSWindow</c>, driven directly through
/// the Objective-C runtime. Implements both the window host and the IPC message transport: JS→C#
/// arrives through a runtime-created <c>WKScriptMessageHandler</c>; C#→JS goes out via
/// <c>-evaluateJavaScript:completionHandler:</c>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacWebViewBackend : IWebViewBackend
{
    // AppKit constants.
    private const nuint NSWindowStyleMaskTitled = 1 << 0;
    private const nuint NSWindowStyleMaskClosable = 1 << 1;
    private const nuint NSWindowStyleMaskMiniaturizable = 1 << 2;
    private const nuint NSWindowStyleMaskResizable = 1 << 3;
    private const nuint NSBackingStoreBuffered = 2;
    private const nint NSApplicationActivationPolicyRegular = 0;
    private const nint WKUserScriptInjectionTimeAtDocumentStart = 0;

    private const string MessageHandlerName = "positron";

    // The runtime-created WKScriptMessageHandler class is process-global and registered once.
    // Accessed via Volatile.Read/Write in the double-checked lock so the stored class pointer is
    // never observed before objc_registerClassPair has completed (IntPtr cannot be marked volatile).
    private static IntPtr s_handlerClass;
    private static readonly object s_classLock = new();

    private IntPtr _window;
    private IntPtr _webView;
    private IntPtr _app;
    private IntPtr _handlerInstance;
    private IntPtr _configuration;
    private NativeCallbackContext<MacWebViewBackend>? _callbackContext;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <inheritdoc />
    public void Initialize(WindowOptions options, string preloadScript)
    {
        _app = Send(Cls("NSApplication"), Sel("sharedApplication"));
        Send(_app, Sel("setActivationPolicy:"), NSApplicationActivationPolicyRegular);

        // 1. Build the configuration and get its user content controller FIRST, so scripts and the
        //    message handler are present before the WKWebView is created with that configuration.
        _configuration = New("WKWebViewConfiguration");
        IntPtr contentController = Msg(_configuration, "userContentController");

        // 2. Register this backend as a resolvable callback owner, create the handler instance, and
        //    attach it under the "positron" name → window.webkit.messageHandlers.positron.
        _callbackContext = new NativeCallbackContext<MacWebViewBackend>(this);
        _handlerInstance = CreateMessageHandlerInstance(_callbackContext.Token);
        Send(contentController, Sel("addScriptMessageHandler:name:"), _handlerInstance, AppKit.NSString(MessageHandlerName));

        // 3. Inject, at document start and in order: (a) the WKWebView-specific transport shim that
        //    defines window.__positronPostToNative on top of the message handler, then (b) the
        //    backend-agnostic Positron bridge which uses it.
        const string transportShim =
            "window.__positronPostToNative = function (json) {"
            + " window.webkit.messageHandlers." + MessageHandlerName + ".postMessage(json); };";
        Send(contentController, Sel("addUserScript:"), CreateUserScript(transportShim));
        Send(contentController, Sel("addUserScript:"), CreateUserScript(preloadScript));

        // 3b. Developer tools + custom app:// scheme serving are configured on the WKWebViewConfiguration
        //     BEFORE the WKWebView is created from it.
        if (options.EnableDevTools)
        {
            IntPtr preferences = Msg(_configuration, "preferences");
            Send(preferences, Sel("setValue:forKey:"),
                Send(Cls("NSNumber"), Sel("numberWithBool:"), (IntPtr)1), AppKit.NSString("developerExtrasEnabled"));
        }

        if (options.ServeDirectory is not null)
        {
            RegisterSchemeHandler(_configuration, options.ServeDirectory, options.ServeEntry);
        }

        // Context-menu suppression is done in JS (cancel the contextmenu event) rather than via a
        // native WKUIDelegate — simplest cross-WebKit approach and no extra runtime class.
        if (options.ContextMenu == ContextMenuMode.Disabled)
        {
            Send(contentController, Sel("addUserScript:"), CreateUserScript(
                "window.addEventListener('contextmenu', function (e) { e.preventDefault(); }, false);"));
        }

        // 4. Create the window and the WKWebView bound to the prepared configuration.
        var frame = new CGRect(0, 0, options.Width, options.Height);
        nuint styleMask = NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable
            | (options.Resizable ? NSWindowStyleMaskResizable : 0);

        IntPtr windowAlloc = Send(Cls("NSWindow"), Sel("alloc"));
        _window = SendWindowInit(windowAlloc, Sel("initWithContentRect:styleMask:backing:defer:"),
            frame, styleMask, NSBackingStoreBuffered, defer: false);
        Send(_window, Sel("setTitle:"), AppKit.NSString(options.Title));

        IntPtr webViewAlloc = Send(Cls("WKWebView"), Sel("alloc"));
        _webView = SendWebViewInit(webViewAlloc, Sel("initWithFrame:configuration:"), frame, _configuration);

        Send(_window, Sel("setContentView:"), _webView);
        Send(_window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        IntPtr nsUrl = Send(Cls("NSURL"), Sel("URLWithString:"), AppKit.NSString(url));
        IntPtr request = Send(Cls("NSURLRequest"), Sel("requestWithURL:"), nsUrl);
        Send(_webView, Sel("loadRequest:"), request);
    }

    /// <inheritdoc />
    public void LoadHtml(string html)
    {
        // A non-nil base URL gives the content a real security origin; with baseURL nil, WKWebView
        // assigns an opaque/unique origin under which some script behaviour is restricted.
        IntPtr baseUrl = Send(Cls("NSURL"), Sel("URLWithString:"), AppKit.NSString("about:blank"));
        Send(_webView, Sel("loadHTMLString:baseURL:"), AppKit.NSString(html), baseUrl);
    }

    /// <inheritdoc />
    public void Evaluate(string javaScript)
    {
        // completionHandler is nil: we never need the result. Passing IntPtr.Zero avoids having to
        // synthesize an Objective-C block.
        Send(_webView, Sel("evaluateJavaScript:completionHandler:"), AppKit.NSString(javaScript), IntPtr.Zero);
    }

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        // Deliver an outbound frame to JS by invoking the bridge receiver with the frame as a
        // string literal, re-encoded through JSON so it is safely escaped.
        string escaped = System.Text.Json.JsonSerializer.Serialize(json, MacJsonContext.Default.String);
        Evaluate($"window.__positronReceive({escaped});");
    }

    /// <inheritdoc />
    public void Run()
    {
        Send(_app, Sel("activateIgnoringOtherApps:"), (IntPtr)1);
        Send(_app, Sel("run"));
    }

    /// <inheritdoc />
    public void Close()
    {
        // Close the window and stop the run loop on the main queue. -stop: takes effect after the
        // next event, so also order the window closed to generate one and unblock -[NSApp run].
        AppKit.DispatchOnMain(() =>
        {
            if (_window != IntPtr.Zero)
            {
                Send(_window, Sel("close"));
            }

            Send(_app, Sel("stop:"), IntPtr.Zero);
        });
    }

    /// <summary>
    /// Pumps the native run loop for a bounded duration without blocking indefinitely. Used by the
    /// integration harness to exercise the real WKWebView message bridge. Unlike <see cref="Run"/>
    /// (which calls <c>-[NSApp run]</c>), this drives <c>CFRunLoopRunInMode</c> in slices so it
    /// always returns — and, crucially, services all CFRunLoop sources (including WebKit's loader
    /// and JavaScript timers), which a bare <c>nextEventMatchingMask:</c> AppKit-event pump does not.
    /// </summary>
    /// <param name="duration">How long to pump the run loop.</param>
    internal void RunFor(TimeSpan duration)
    {
        // -[NSApp run] normally performs finishLaunching once; do it ourselves for a manual pump.
        Send(_app, Sel("finishLaunching"));
        Send(_app, Sel("activateIgnoringOtherApps:"), (IntPtr)1);

        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            // Run the default mode for a 50ms slice, returning after it handles a source. Driving
            // CFRunLoop (not just the AppKit event queue) services WebKit's loader and JS timers.
            _ = CoreFoundation.CFRunLoopRunInMode(CoreFoundation.DefaultMode, 0.05, returnAfterSourceHandled: true);
        }
    }

    /// <summary>Reads the WKWebView's current document title (<c>document.title</c>). Used by the
    /// integration harness to observe values the web content wrote after an IPC round trip. Note
    /// this is the web view's title, not the NSWindow title — they are independent.</summary>
    internal string GetTitle() => AppKit.FromNSString(Msg(_webView, "title"));

    /// <inheritdoc />
    public void Dispose()
    {
        // H1: drop the static instance→token map entry so it does not leak for the process lifetime.
        if (_handlerInstance != IntPtr.Zero)
        {
            s_instances.TryRemove(_handlerInstance, out _);
        }

        // H2: release the Objective-C objects this backend owns (alloc/init/New give +1 retain).
        // The window is closed; released objects are set to nil to guard against double-release.
        if (_window != IntPtr.Zero)
        {
            Send(_window, Sel("close"));
            Send(_window, Sel("release"));
            _window = IntPtr.Zero;
        }

        if (_webView != IntPtr.Zero)
        {
            Send(_webView, Sel("release"));
            _webView = IntPtr.Zero;
        }

        if (_handlerInstance != IntPtr.Zero)
        {
            Send(_handlerInstance, Sel("release"));
            _handlerInstance = IntPtr.Zero;
        }

        if (_configuration != IntPtr.Zero)
        {
            Send(_configuration, Sel("release"));
            _configuration = IntPtr.Zero;
        }

        _callbackContext?.Dispose();
        _callbackContext = null;
    }

    private static IntPtr CreateUserScript(string source)
    {
        IntPtr alloc = Send(Cls("WKUserScript"), Sel("alloc"));
        return SendUserScriptInit(alloc, Sel("initWithSource:injectionTime:forMainFrameOnly:"),
            AppKit.NSString(source), WKUserScriptInjectionTimeAtDocumentStart, forMainFrameOnly: true);
    }

    /// <summary>
    /// Creates an instance of the (lazily registered) WKScriptMessageHandler subclass and stores
    /// the resolvable callback token in an associated ivar-free manner: the token is captured by
    /// the single process-global handler via the message's webView association. Here we keep it
    /// simple — the handler resolves the owner through the static map keyed by the instance.
    /// </summary>
    private IntPtr CreateMessageHandlerInstance(IntPtr token)
    {
        EnsureHandlerClass();
        IntPtr instance = Send(Send(Volatile.Read(ref s_handlerClass), Sel("alloc")), Sel("init"));
        s_instances[instance] = token;
        return instance;
    }

    // Maps a handler instance pointer → callback token, so the static IMP can resolve the owning
    // backend. Concurrent because instances are created on the main thread but read in callbacks.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, IntPtr> s_instances = new();

    private static void EnsureHandlerClass()
    {
        if (Volatile.Read(ref s_handlerClass) != IntPtr.Zero)
        {
            return;
        }

        lock (s_classLock)
        {
            if (Volatile.Read(ref s_handlerClass) != IntPtr.Zero)
            {
                return;
            }

            IntPtr cls = objc_allocateClassPair(Cls("NSObject"), "PositronScriptMessageHandler", 0);
            class_addProtocol(cls, objc_getProtocol("WKScriptMessageHandler"));

            // -userContentController:didReceiveScriptMessage:  type encoding "v@:@@"
            unsafe
            {
                delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> imp = &OnScriptMessage;
                class_addMethod(cls, Sel("userContentController:didReceiveScriptMessage:"), (IntPtr)imp, "v@:@@");
            }

            objc_registerClassPair(cls);
            // Publish only after full registration so another thread never sees a half-built class.
            Volatile.Write(ref s_handlerClass, cls);
        }
    }

    /// <summary>
    /// The native IMP for <c>-userContentController:didReceiveScriptMessage:</c>. Static and
    /// blittable-only per <c>[UnmanagedCallersOnly]</c>; resolves the owning backend from the
    /// instance→token map and forwards the message body (a JSON string) to the router.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnScriptMessage(IntPtr self, IntPtr sel, IntPtr userContentController, IntPtr message)
    {
        if (!s_instances.TryGetValue(self, out IntPtr token))
        {
            return;
        }

        MacWebViewBackend? backend = NativeCallbackContext<MacWebViewBackend>.Resolve(token);
        if (backend is null)
        {
            return;
        }

        IntPtr body = Send(message, Sel("body"));
        string json = AppKit.FromNSString(body);
        backend.MessageReceived?.Invoke(json);
    }
}

/// <summary>Source-generated context for the one string the Mac backend serializes.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class MacJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}