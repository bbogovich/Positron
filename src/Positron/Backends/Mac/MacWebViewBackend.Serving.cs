using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Positron.Interop;
using Positron.Serving;

using static Positron.Backends.Mac.ObjC;

namespace Positron.Backends.Mac;

/// <summary>
/// macOS <c>app://</c> scheme serving for <see cref="MacWebViewBackend"/>. Registers a runtime-created
/// <c>WKURLSchemeHandler</c> on the configuration that answers requests from a served directory via
/// <see cref="StaticFileResolver"/> — giving a built SPA a real web origin, correct MIME types, and
/// history-API fallback.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacWebViewBackend
{
    /// <summary>The scheme consumers load: <c>app://localhost/</c>.</summary>
    public const string AppScheme = "app";

    private static IntPtr s_schemeHandlerClass;
    private static readonly object s_schemeLock = new();

    // Maps a scheme-handler instance pointer → the owning backend's callback token.
    private static readonly ConcurrentDictionary<IntPtr, IntPtr> s_schemeOwners = new();

    private StaticFileResolver? _resolver;
    private IntPtr _schemeHandlerInstance;
    private NativeCallbackContext<MacWebViewBackend>? _schemeContext;

    private void RegisterSchemeHandler(IntPtr configuration, string rootPath, string entry)
    {
        _resolver = new StaticFileResolver(rootPath, entry);
        EnsureSchemeHandlerClass();

        _schemeContext = new NativeCallbackContext<MacWebViewBackend>(this);
        _schemeHandlerInstance = Send(Send(Volatile.Read(ref s_schemeHandlerClass), Sel("alloc")), Sel("init"));
        s_schemeOwners[_schemeHandlerInstance] = _schemeContext.Token;

        Send(configuration, Sel("setURLSchemeHandler:forURLScheme:"),
            _schemeHandlerInstance, AppKit.NSString(AppScheme));
    }

    private static void EnsureSchemeHandlerClass()
    {
        if (Volatile.Read(ref s_schemeHandlerClass) != IntPtr.Zero)
        {
            return;
        }

        lock (s_schemeLock)
        {
            if (Volatile.Read(ref s_schemeHandlerClass) != IntPtr.Zero)
            {
                return;
            }

            IntPtr cls = objc_allocateClassPair(Cls("NSObject"), "PositronSchemeHandler", 0);
            class_addProtocol(cls, objc_getProtocol("WKURLSchemeHandler"));

            unsafe
            {
                delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> startImp = &OnStartSchemeTask;
                class_addMethod(cls, Sel("webView:startURLSchemeTask:"), (IntPtr)startImp, "v@:@@");

                delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> stopImp = &OnStopSchemeTask;
                class_addMethod(cls, Sel("webView:stopURLSchemeTask:"), (IntPtr)stopImp, "v@:@@");
            }

            objc_registerClassPair(cls);
            Volatile.Write(ref s_schemeHandlerClass, cls);
        }
    }

    /// <summary>
    /// IMP for <c>-webView:startURLSchemeTask:</c>. Resolves the request path against the served
    /// directory and answers the task with the file's bytes + content type, or a 404.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStartSchemeTask(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        if (!s_schemeOwners.TryGetValue(self, out IntPtr token))
        {
            return;
        }

        MacWebViewBackend? backend = NativeCallbackContext<MacWebViewBackend>.Resolve(token);
        StaticFileResolver? resolver = backend?._resolver;
        if (resolver is null)
        {
            return;
        }

        // Extract the request URL path: task.request.URL.path
        IntPtr request = Send(task, Sel("request"));
        IntPtr url = Send(request, Sel("URL"));
        string path = AppKit.FromNSString(Send(url, Sel("path")));

        if (resolver.TryResolve(path, out string? filePath, out string contentType))
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            RespondWithData(task, url, bytes, contentType);
        }
        else
        {
            RespondNotFound(task, url);
        }
    }

    /// <summary>IMP for <c>-webView:stopURLSchemeTask:</c>. We answer synchronously, so this is a no-op.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStopSchemeTask(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        // No async work to cancel: start answers the task before returning.
    }

    private static void RespondWithData(IntPtr task, IntPtr url, byte[] bytes, string contentType)
    {
        // Build an NSHTTPURLResponse with 200 + Content-Type/Content-Length headers.
        IntPtr headers = Send(Cls("NSMutableDictionary"), Sel("dictionary"));
        Send(headers, Sel("setObject:forKey:"), AppKit.NSString(contentType), AppKit.NSString("Content-Type"));
        Send(headers, Sel("setObject:forKey:"),
            AppKit.NSString(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            AppKit.NSString("Content-Length"));

        IntPtr response = SendHttpResponseInit(Send(Cls("NSHTTPURLResponse"), Sel("alloc")),
            Sel("initWithURL:statusCode:HTTPVersion:headerFields:"),
            url, 200, AppKit.NSString("HTTP/1.1"), headers);

        IntPtr data = NSDataFromBytes(bytes);

        Send(task, Sel("didReceiveResponse:"), response);
        Send(task, Sel("didReceiveData:"), data);
        Send(task, Sel("didFinish"));
        Send(response, Sel("release"));
    }

    private static void RespondNotFound(IntPtr task, IntPtr url)
    {
        IntPtr response = SendHttpResponseInit(Send(Cls("NSHTTPURLResponse"), Sel("alloc")),
            Sel("initWithURL:statusCode:HTTPVersion:headerFields:"),
            url, 404, AppKit.NSString("HTTP/1.1"), IntPtr.Zero);
        Send(task, Sel("didReceiveResponse:"), response);
        Send(task, Sel("didFinish"));
        Send(response, Sel("release"));
    }

    private static IntPtr NSDataFromBytes(byte[] bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                return SendDataInit(Send(Cls("NSData"), Sel("alloc")),
                    Sel("initWithBytes:length:"), (IntPtr)p, (nuint)bytes.Length);
            }
        }
    }
}