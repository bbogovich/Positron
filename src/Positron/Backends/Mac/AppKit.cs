using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using static Positron.Backends.Mac.ObjC;

namespace Positron.Backends.Mac;

/// <summary>
/// Foundation/AppKit helpers built on top of the raw <see cref="ObjC"/> runtime bindings:
/// NSString marshalling and libdispatch main-queue dispatch.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class AppKit
{
    /// <summary>Creates an autoreleased <c>NSString</c> from a managed UTF-16 string.</summary>
    public static IntPtr NSString(string value)
    {
        // +[NSString stringWithUTF8String:] copies the bytes, so a pinned UTF-8 buffer is safe.
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        unsafe
        {
            fixed (byte* p = utf8)
            {
                return Send(Cls("NSString"), Sel("stringWithUTF8String:"), (IntPtr)p);
            }
        }
    }

    /// <summary>Reads an <c>NSString</c> back into a managed string via <c>-UTF8String</c>.</summary>
    public static string FromNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        IntPtr utf8 = Send(nsString, Sel("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
    }

    /// <summary>Whether the calling thread is the process main thread (the AppKit UI thread).</summary>
    public static bool IsMainThread => pthread_main_np() != 0;

    /// <summary>
    /// Runs <paramref name="action"/> now if called on the main thread, otherwise schedules it on the
    /// main dispatch queue. AppKit and WebKit objects must only be touched from the main thread.
    /// </summary>
    public static void RunOnMain(Action action)
    {
        if (IsMainThread)
        {
            action();
        }
        else
        {
            DispatchOnMain(action);
        }
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the main thread and returns its result as a task: completed
    /// synchronously when already on the main thread, otherwise when the main queue has run it.
    /// </summary>
    public static Task<T> InvokeOnMainAsync<T>(Func<T> func)
    {
        if (IsMainThread)
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMain(() =>
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

    /// <summary>Schedules an action on the main dispatch queue (required for all UI/WebKit calls).</summary>
    public static void DispatchOnMain(Action action)
    {
        var ctx = new DispatchContext(action);
        unsafe
        {
            dispatch_async_f(MainQueue, ctx.ToPointer(), &Trampoline);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void Trampoline(IntPtr context)
    {
        DispatchContext? ctx = DispatchContext.FromPointer(context);
        ctx?.Invoke();
    }

    // dispatch_get_main_queue() is a macro resolving to the global data symbol _dispatch_main_q;
    // it is not a callable function. Resolve the symbol's address from libSystem once.
    private static readonly IntPtr MainQueue = ResolveMainQueue();

    private static IntPtr ResolveMainQueue()
    {
        IntPtr lib = NativeLibrary.Load("/usr/lib/libSystem.dylib");
        return NativeLibrary.GetExport(lib, "_dispatch_main_q");
    }

    [LibraryImport("/usr/lib/libSystem.dylib")]
    private static partial int pthread_main_np();

    [LibraryImport("/usr/lib/libSystem.dylib")]
    private static unsafe partial void dispatch_async_f(
        IntPtr queue, IntPtr context, delegate* unmanaged[Cdecl]<IntPtr, void> work);

    /// <summary>Boxes a managed action behind a GCHandle for a one-shot libdispatch trampoline.</summary>
    private sealed class DispatchContext
    {
        private readonly Action _action;
        private GCHandle _handle;

        public DispatchContext(Action action)
        {
            _action = action;
            _handle = GCHandle.Alloc(this);
        }

        public IntPtr ToPointer() => GCHandle.ToIntPtr(_handle);

        public static DispatchContext? FromPointer(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            GCHandle h = GCHandle.FromIntPtr(ptr);
            return h.IsAllocated ? h.Target as DispatchContext : null;
        }

        public void Invoke()
        {
            try
            {
                _action();
            }
            catch (Exception ex)
            {
                // This runs from a native libdispatch callback: an exception escaping here would
                // terminate the process, so report it instead.
                Console.Error.WriteLine($"positron: main-queue callback failed: {ex}");
            }
            finally
            {
                if (_handle.IsAllocated)
                {
                    _handle.Free();
                }
            }
        }
    }
}