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
/// <c>-evaluateJavaScript:completionHandler:</c>. A runtime-created <c>NSWindowDelegate</c> reports
/// the window closing (however it was closed) and ends the run loop. Calls from non-main threads are
/// marshalled to the main queue; messages are exchanged only with trusted main-frame documents.
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
    private static IntPtr s_delegateClass;
    private static readonly object s_classLock = new();

    private readonly TrustedOrigins _trust = new();
    private IntPtr _windowDelegate;
    private int _closedRaised;

    private IntPtr _window;
    private IntPtr _webView;
    private IntPtr _app;
    private IntPtr _handlerInstance;
    private IntPtr _configuration;
    private NativeCallbackContext<MacWebViewBackend>? _callbackContext;

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <inheritdoc />
    public event Action? Closed;

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
            _trust.Trust("app://localhost/");
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

        // This backend owns the window (+1 from alloc/init) and releases it in Dispose, so AppKit
        // must not also release it when it closes (NSWindow's default is releasedWhenClosed = YES).
        SendBool(_window, Sel("setReleasedWhenClosed:"), false);

        // The delegate turns a user close (title-bar button, Cmd-W) into Closed + run-loop exit;
        // without it -[NSApp run] keeps running with no window and App.Run never returns.
        _windowDelegate = CreateWindowDelegateInstance(_callbackContext.Token);
        Send(_window, Sel("setDelegate:"), _windowDelegate);

        IntPtr webViewAlloc = Send(Cls("WKWebView"), Sel("alloc"));
        _webView = SendWebViewInit(webViewAlloc, Sel("initWithFrame:configuration:"), frame, _configuration);

        Send(_window, Sel("setContentView:"), _webView);
        Send(_window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        _trust.Trust(url);
        AppKit.RunOnMain(() =>
        {
            IntPtr nsUrl = Send(Cls("NSURL"), Sel("URLWithString:"), AppKit.NSString(url));
            IntPtr request = Send(Cls("NSURLRequest"), Sel("requestWithURL:"), nsUrl);
            Send(_webView, Sel("loadRequest:"), request);
        });
    }

    /// <inheritdoc />
    public void LoadHtml(string html)
    {
        // A non-nil base URL gives the content a real security origin; with baseURL nil, WKWebView
        // assigns an opaque/unique origin under which some script behaviour is restricted.
        _trust.Trust("about:blank");
        AppKit.RunOnMain(() =>
        {
            IntPtr baseUrl = Send(Cls("NSURL"), Sel("URLWithString:"), AppKit.NSString("about:blank"));
            Send(_webView, Sel("loadHTMLString:baseURL:"), AppKit.NSString(html), baseUrl);
        });
    }

    /// <inheritdoc />
    public void Evaluate(string javaScript)
    {
        // completionHandler is nil: we never need the result. Passing IntPtr.Zero avoids having to
        // synthesize an Objective-C block. WKWebView is main-thread-only.
        AppKit.RunOnMain(() =>
            Send(_webView, Sel("evaluateJavaScript:completionHandler:"), AppKit.NSString(javaScript), IntPtr.Zero));
    }

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        // Deliver an outbound frame to JS by invoking the bridge receiver with the frame as a
        // string literal, re-encoded through JSON so it is safely escaped.
        string escaped = System.Text.Json.JsonSerializer.Serialize(json, MacJsonContext.Default.String);
        AppKit.RunOnMain(() =>
        {
            // Never hand results or events to a document the host did not load (e.g. a remote page
            // the user navigated to): it would receive app data and could reply.
            if (_webView == IntPtr.Zero || !_trust.IsTrusted(CurrentUrl()))
            {
                return;
            }

            Send(_webView, Sel("evaluateJavaScript:completionHandler:"),
                AppKit.NSString($"window.__positronReceive({escaped});"), IntPtr.Zero);
        });
    }

    /// <summary>The main frame's current URL (<c>-[WKWebView URL]</c>), or null. Main thread only.</summary>
    private string? CurrentUrl()
    {
        IntPtr url = Send(_webView, Sel("URL"));
        return url == IntPtr.Zero ? null : AppKit.FromNSString(Send(url, Sel("absoluteString")));
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
        // Close the window and stop the run loop on the main queue. Closing the window runs the
        // delegate's windowWillClose: (raising Closed); -stop: takes effect after the next event, so
        // ordering the window closed also generates one and unblocks -[NSApp run].
        AppKit.DispatchOnMain(() =>
        {
            if (_window != IntPtr.Zero)
            {
                Send(_window, Sel("close"));
            }

            StopRunLoop(_app);
        });
    }

    /// <summary>
    /// Stops <c>-[NSApp run]</c>. <c>-stop:</c> only takes effect once AppKit finishes processing an
    /// event; when it is called from a dispatch-queue block (programmatic close) rather than during
    /// event handling (a user click), no further event may ever arrive and the run loop would hang.
    /// Posting an empty application-defined event guarantees the loop wakes and returns.
    /// </summary>
    private static void StopRunLoop(IntPtr app)
    {
        const nuint NSEventTypeApplicationDefined = 15;

        Send(app, Sel("stop:"), IntPtr.Zero);
        IntPtr wake = SendOtherEvent(Cls("NSEvent"),
            Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            NSEventTypeApplicationDefined, default, 0, 0, 0, IntPtr.Zero, 0, 0, 0);
        SendIdBool(app, Sel("postEvent:atStart:"), wake, true);
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

    /// <summary>
    /// Test hook: presses the window's close button programmatically (<c>-performClose:</c>), which
    /// takes the same path as a user click. Used by the integration harness to verify that a user
    /// close raises <see cref="Closed"/> and ends <see cref="Run"/>.
    /// </summary>
    internal void PerformCloseForTest() =>
        AppKit.DispatchOnMain(() => Send(_window, Sel("performClose:"), IntPtr.Zero));

    /// <summary>
    /// Test hook: loads <paramref name="html"/> under <paramref name="baseUrl"/>'s origin WITHOUT
    /// trusting it, standing in for a remote page the user navigated to, so the harness can check
    /// that such a document cannot reach any channel. Main thread only.
    /// </summary>
    internal void LoadHtmlUntrustedForTest(string html, string baseUrl)
    {
        IntPtr nsBase = Send(Cls("NSURL"), Sel("URLWithString:"), AppKit.NSString(baseUrl));
        Send(_webView, Sel("loadHTMLString:baseURL:"), AppKit.NSString(html), nsBase);
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

        // Detach the delegate first so closing the window below does not raise Closed during Dispose.
        if (_windowDelegate != IntPtr.Zero)
        {
            s_instances.TryRemove(_windowDelegate, out _);
            if (_window != IntPtr.Zero)
            {
                Send(_window, Sel("setDelegate:"), IntPtr.Zero);
            }

            Send(_windowDelegate, Sel("release"));
            _windowDelegate = IntPtr.Zero;
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

    /// <summary>Creates an instance of the (lazily registered) NSWindowDelegate subclass.</summary>
    private static IntPtr CreateWindowDelegateInstance(IntPtr token)
    {
        EnsureDelegateClass();
        IntPtr instance = Send(Send(Volatile.Read(ref s_delegateClass), Sel("alloc")), Sel("init"));
        s_instances[instance] = token;
        return instance;
    }

    private static void EnsureDelegateClass()
    {
        if (Volatile.Read(ref s_delegateClass) != IntPtr.Zero)
        {
            return;
        }

        lock (s_classLock)
        {
            if (Volatile.Read(ref s_delegateClass) != IntPtr.Zero)
            {
                return;
            }

            IntPtr cls = objc_allocateClassPair(Cls("NSObject"), "PositronWindowDelegate", 0);
            class_addProtocol(cls, objc_getProtocol("NSWindowDelegate"));

            // -windowWillClose:(NSNotification *)  type encoding "v@:@"
            unsafe
            {
                delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> imp = &OnWindowWillClose;
                class_addMethod(cls, Sel("windowWillClose:"), (IntPtr)imp, "v@:@");
            }

            objc_registerClassPair(cls);
            Volatile.Write(ref s_delegateClass, cls);
        }
    }

    /// <summary>
    /// The native IMP for <c>-windowWillClose:</c>. Raises <see cref="Closed"/> once and stops the
    /// application run loop so <see cref="Run"/> (and therefore <c>App.Run</c>) returns.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWindowWillClose(IntPtr self, IntPtr sel, IntPtr notification)
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

        if (Interlocked.Exchange(ref backend._closedRaised, 1) == 0)
        {
            try
            {
                backend.Closed?.Invoke();
            }
            catch (Exception ex)
            {
                // An exception escaping a native callback terminates the process; report it instead.
                Console.Error.WriteLine($"positron: Closed handler threw: {ex}");
            }
        }

        StopRunLoop(backend._app);
    }

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

        // Accept messages only from the main frame of a document the host loaded. WebKit exposes
        // window.webkit.messageHandlers to every frame and every origin, so without this check an
        // iframe or a remote page navigated to could call any channel (including positron.fs.*).
        IntPtr frameInfo = Send(message, Sel("frameInfo"));
        bool isMainFrame = frameInfo != IntPtr.Zero && ((nint)Send(frameInfo, Sel("isMainFrame")) & 0xFF) != 0;
        if (!isMainFrame || !backend._trust.IsTrusted(backend.CurrentUrl()))
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