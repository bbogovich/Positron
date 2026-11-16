using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Positron.Backends.Mac;

/// <summary>
/// Thin bindings over the Objective-C runtime (<c>/usr/lib/libobjc.A.dylib</c>) used to drive
/// AppKit and WebKit without a managed wrapper framework.
/// </summary>
/// <remarks>
/// The key correctness constraint is <c>objc_msgSend</c>: it is a trampoline, not a real variadic
/// function, so it must be declared once per distinct call signature and never bound through the
/// C# variadic path. On arm64 all calls (including struct and floating-point returns) go through
/// plain <c>objc_msgSend</c>; the x86_64 <c>_stret</c>/<c>_fpret</c> variants are not needed on the
/// Apple-Silicon-first target and are omitted. Each signature below is a separate
/// <see cref="DllImportAttribute"/> with <c>EntryPoint = "objc_msgSend"</c>.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // AppKit and WebKit classes are only visible once their frameworks are loaded into the process.
    // A bare console host does not link them, so objc_getClass("NSApplication") returns null until
    // we dlopen them. Done once, eagerly, before any class lookup.
    private static readonly bool s_frameworksLoaded = LoadFrameworks();

    private static bool LoadFrameworks()
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
        return true;
    }

    // ── Runtime primitives ────────────────────────────────────────────────────────────────

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getProtocol(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC)]
    public static partial void objc_registerClassPair(IntPtr cls);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    [LibraryImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool class_addProtocol(IntPtr cls, IntPtr protocol);

    // ── objc_msgSend: one declaration per concrete signature ────────────────────────────────

    /// <summary>objc_msgSend(id, SEL) → id</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary>objc_msgSend(id, SEL, id) → id</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1);

    /// <summary>objc_msgSend(id, SEL, id, id) → id</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    /// <summary>objc_msgSend(id, SEL, NSInteger) → id/void</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendInt(IntPtr receiver, IntPtr selector, nint arg1);

    /// <summary>objc_msgSend(id, SEL, bool) → void</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool arg1);

    /// <summary>objc_msgSend(id, SEL, CGRect, NSUInteger, NSUInteger, bool) → id (NSWindow init).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendWindowInit(
        IntPtr receiver, IntPtr selector, CGRect frame, nuint styleMask, nuint backing,
        [MarshalAs(UnmanagedType.U1)] bool defer);

    /// <summary>objc_msgSend(id, SEL, CGRect, id) → id (WKWebView initWithFrame:configuration:).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendWebViewInit(IntPtr receiver, IntPtr selector, CGRect frame, IntPtr configuration);

    /// <summary>objc_msgSend(id, SEL, id, NSInteger, bool) → id (WKUserScript init).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendUserScriptInit(
        IntPtr receiver, IntPtr selector, IntPtr source, nint injectionTime,
        [MarshalAs(UnmanagedType.U1)] bool forMainFrameOnly);

    /// <summary>objc_msgSend(id, SEL, id) → id, where arg1 is a 0-based index (NSArray objectAtIndex:).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

    /// <summary>objc_msgSend(id, SEL, const void*, NSUInteger) → id (NSData initWithBytes:length:).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendDataInit(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>
    /// objc_msgSend(id, SEL, id url, NSInteger status, id httpVersion, id headers) → id
    /// (NSHTTPURLResponse initWithURL:statusCode:HTTPVersion:headerFields:).
    /// </summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendHttpResponseInit(
        IntPtr receiver, IntPtr selector, IntPtr url, nint statusCode, IntPtr httpVersion, IntPtr headers);

    /// <summary>
    /// objc_msgSend(id, SEL, NSUInteger mask, id untilDate, id mode, bool dequeue) → id (NSEvent).
    /// Drives <c>-[NSApp nextEventMatchingMask:untilDate:inMode:dequeue:]</c> for a manual pump.
    /// </summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendNextEvent(
        IntPtr receiver, IntPtr selector, nuint mask, IntPtr untilDate, IntPtr mode,
        [MarshalAs(UnmanagedType.U1)] bool dequeue);

    /// <summary>objc_msgSend(id, SEL, double) → id (e.g. +[NSDate dateWithTimeIntervalSinceNow:]).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendDouble(IntPtr receiver, IntPtr selector, double arg1);

    // ── Selector / class helpers (cached) ───────────────────────────────────────────────────
    // Concurrent because Sel("body") is resolved from the WebKit callback thread in OnScriptMessage,
    // not only on the main thread during Initialize.

    private static readonly ConcurrentDictionary<string, IntPtr> s_selCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IntPtr> s_clsCache = new(StringComparer.Ordinal);

    /// <summary>Registers (and caches) a selector by name.</summary>
    public static IntPtr Sel(string name) => s_selCache.GetOrAdd(name, sel_registerName);

    /// <summary>Looks up (and caches) a class by name.</summary>
    public static IntPtr Cls(string name)
    {
        // Touch the framework-loading initializer so AppKit/WebKit classes are resolvable.
        _ = s_frameworksLoaded;
        return s_clsCache.GetOrAdd(name, objc_getClass);
    }

    /// <summary>Sends a parameterless message by selector name.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr Msg(IntPtr receiver, string selector) => Send(receiver, Sel(selector));

    /// <summary>Sends a one-object-argument message by selector name.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr Msg(IntPtr receiver, string selector, IntPtr arg1) => Send(receiver, Sel(selector), arg1);

    /// <summary>Allocates and initializes an instance of the named class (<c>[[Cls alloc] init]</c>).</summary>
    public static IntPtr New(string className)
    {
        IntPtr instance = Send(Cls(className), Sel("alloc"));
        return Send(instance, Sel("init"));
    }
}

/// <summary>
/// Mirror of Core Graphics <c>CGRect</c> — four 64-bit doubles (CGFloat is 64-bit on 64-bit macOS),
/// laid out sequentially so it is blittable across the <c>objc_msgSend</c> boundary.
/// </summary>
[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public double X;
    public double Y;
    public double Width;
    public double Height;

    public CGRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}