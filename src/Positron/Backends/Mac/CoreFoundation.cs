using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Positron.Backends.Mac;

/// <summary>
/// Minimal Core Foundation bindings for driving the run loop in bounded slices. Used by the
/// bounded-run harness so WebKit's loader and JavaScript timer sources are serviced (an AppKit
/// event-queue pump alone does not run them).
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class CoreFoundation
{
    private const string CoreFoundationLib =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>The CFRunLoop default mode (<c>kCFRunLoopDefaultMode</c>), resolved once.</summary>
    public static readonly IntPtr DefaultMode = ResolveDefaultMode();

    /// <summary>
    /// Runs the current thread's run loop in the given mode for up to <paramref name="seconds"/>.
    /// </summary>
    /// <returns>A CFRunLoopRunResult value (finished / stopped / timed-out / handled-source).</returns>
    [LibraryImport(CoreFoundationLib)]
    public static partial int CFRunLoopRunInMode(
        IntPtr mode, double seconds, [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    private static IntPtr ResolveDefaultMode()
    {
        IntPtr lib = NativeLibrary.Load(CoreFoundationLib);
        // kCFRunLoopDefaultMode is an exported CFStringRef* — dereference the symbol to get the value.
        IntPtr symbol = NativeLibrary.GetExport(lib, "kCFRunLoopDefaultMode");
        return Marshal.ReadIntPtr(symbol);
    }
}