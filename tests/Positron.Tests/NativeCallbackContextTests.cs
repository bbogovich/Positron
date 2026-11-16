using Positron.Interop;

namespace Positron.Tests;

/// <summary>
/// Unit tests for <see cref="NativeCallbackContext{T}"/> — the GCHandle round-trip that lets a
/// static <c>[UnmanagedCallersOnly]</c> native callback recover its owning managed object. This is
/// platform-agnostic (pure <c>GCHandle</c>) and flagged in its own doc as the most error-prone part
/// of the interop layer, so it is exercised directly here.
/// </summary>
public sealed class NativeCallbackContextTests
{
    private sealed class Owner
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Ctor_NullOwner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new NativeCallbackContext<Owner>(null!));

    [Fact]
    public void Token_IsNonZero_AfterAlloc()
    {
        using var ctx = new NativeCallbackContext<Owner>(new Owner());
        Assert.NotEqual(IntPtr.Zero, ctx.Token);
    }

    [Fact]
    public void Resolve_Token_ReturnsSameOwnerInstance()
    {
        var owner = new Owner { Value = 42 };
        using var ctx = new NativeCallbackContext<Owner>(owner);

        Owner? resolved = NativeCallbackContext<Owner>.Resolve(ctx.Token);

        Assert.Same(owner, resolved);
        Assert.Equal(42, resolved!.Value);
    }

    [Fact]
    public void Resolve_Zero_ReturnsNull() =>
        Assert.Null(NativeCallbackContext<Owner>.Resolve(IntPtr.Zero));

    [Fact]
    public void Resolve_AfterDispose_ReturnsNull()
    {
        var ctx = new NativeCallbackContext<Owner>(new Owner());
        IntPtr token = ctx.Token;
        ctx.Dispose();

        // The handle is freed; resolving a freed token must not throw and must not return an owner.
        Owner? resolved = NativeCallbackContext<Owner>.Resolve(token);
        Assert.Null(resolved);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var ctx = new NativeCallbackContext<Owner>(new Owner());
        ctx.Dispose();
        ctx.Dispose();
    }
}