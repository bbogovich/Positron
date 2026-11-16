using System.Runtime.InteropServices;

namespace Positron.Interop;

/// <summary>
/// Bridges the gap between a static <c>[UnmanagedCallersOnly]</c> native callback — which cannot
/// capture managed state — and the owning managed object. The owner is pinned behind a
/// <see cref="GCHandle"/> whose <see cref="IntPtr"/> token is threaded through the native API as
/// an opaque user-context pointer and resolved back inside the callback.
/// </summary>
/// <remarks>
/// This is the single most error-prone part of the native interop layer and is shared by every
/// backend (the ObjC IMP, the GObject signal, and the WebView2 COM handler). Always
/// <see cref="Dispose"/> on teardown; a leaked handle both leaks the owner and keeps the native
/// side pointing at memory the GC will never reclaim.
/// </remarks>
/// <typeparam name="T">The managed owner type (a backend instance).</typeparam>
internal sealed class NativeCallbackContext<T> : IDisposable
    where T : class
{
    private GCHandle _handle;

    /// <summary>Pins <paramref name="owner"/> and allocates a resolvable token.</summary>
    public NativeCallbackContext(T owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _handle = GCHandle.Alloc(owner);
    }

    /// <summary>
    /// The opaque token to pass to the native API as the callback's user-context argument.
    /// </summary>
    public IntPtr Token => GCHandle.ToIntPtr(_handle);

    /// <summary>
    /// Resolves the owner from a token received inside a native callback.
    /// </summary>
    /// <param name="token">The user-context pointer the native side passed back.</param>
    /// <returns>The owner, or null if the token is zero or no longer points at a live handle.</returns>
    public static T? Resolve(IntPtr token)
    {
        if (token == IntPtr.Zero)
        {
            return null;
        }

        GCHandle handle = GCHandle.FromIntPtr(token);
        return handle.IsAllocated ? handle.Target as T : null;
    }

    /// <summary>Frees the pinning handle. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }
}