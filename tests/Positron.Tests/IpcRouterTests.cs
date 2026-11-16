using System.Text.Json;
using System.Text.Json.Serialization;

using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Unit tests for <see cref="IpcRouter"/> covering the JSON contract, channel registration,
/// handler dispatch, event emission, guard clauses, and error paths — all without a WebView.
/// </summary>
public sealed partial class IpcRouterTests
{
    // A source-generated context so the tests themselves run AOT-clean and model how an app
    // supplies its own types.
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(EchoArg))]
    [JsonSerializable(typeof(EchoResult))]
    private sealed partial class TestJsonContext : JsonSerializerContext
    {
    }

    public sealed record EchoArg(string Message);

    public sealed record EchoResult(string Echoed, int Length);

    private static JsonSerializerOptions Options =>
        new() { TypeInfoResolver = TestJsonContext.Default };

    private static (IpcRouter Router, FakeTransport Transport) CreateRouter(
        Action<string, Exception>? onHandlerError = null)
    {
        var transport = new FakeTransport();
        var router = new IpcRouter(transport, Options, onHandlerError);
        return (router, transport);
    }

    private static IpcMessage Parse(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.IpcMessage)!;

    private static string InvokeFrame(long? seq, string? channel, object? payload, bool includePayload = true)
    {
        var msg = new IpcMessage
        {
            Kind = IpcMessageKind.Invoke,
            Seq = seq,
            Channel = channel,
            Payload = includePayload && payload is not null
                ? JsonSerializer.SerializeToElement(payload, Options)
                : null,
        };
        return JsonSerializer.Serialize(msg, IpcJsonContext.Default.IpcMessage);
    }

    // ── Constructor guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_NullTransport_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new IpcRouter(null!, Options));

    [Fact]
    public void Ctor_NullOptions_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new IpcRouter(new FakeTransport(), null!));

    // ── Happy paths ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_RoutesToHandler_AndReturnsResult()
    {
        var (router, transport) = CreateRouter();
        router.Handle<EchoArg, EchoResult>("echo", arg => new EchoResult(arg.Message, arg.Message.Length));

        transport.Receive(InvokeFrame(1, "echo", new EchoArg("hello")));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.Equal(IpcMessageKind.Result, result.Kind);
        Assert.Equal(1, result.Seq);
        Assert.True(result.Ok);
        Assert.Null(result.Error);

        EchoResult payload = result.Payload!.Value.Deserialize<EchoResult>(Options)!;
        Assert.Equal("hello", payload.Echoed);
        Assert.Equal(5, payload.Length);
    }

    [Fact]
    public async Task Invoke_AsyncHandler_IsAwaited()
    {
        var (router, transport) = CreateRouter();
        router.Handle<int, int>("double", async n =>
        {
            await Task.Delay(10);
            return n * 2;
        });

        transport.Receive(InvokeFrame(7, "double", 21));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.True(result.Ok);
        Assert.Equal(42, result.Payload!.Value.Deserialize<int>(Options));
    }

    [Fact]
    public async Task Invoke_WithNoPayload_RejectsRatherThanCrashing()
    {
        // An invoke with no payload cannot be deserialized into a concrete arg type. The router
        // must convert that into a clean rejection, never an unobserved async-void fault.
        var (router, transport) = CreateRouter();
        router.Handle<int, int>("ping", n => n);

        transport.Receive(InvokeFrame(2, "ping", payload: null, includePayload: false));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.Equal(IpcMessageKind.Result, result.Kind);
        Assert.Equal(2, result.Seq);
        Assert.False(result.Ok);
    }

    // ── Registration guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Handle_DuplicateChannel_Throws()
    {
        var (router, _) = CreateRouter();
        router.Handle<int, int>("x", n => n);

        ArgumentException ex = Assert.Throws<ArgumentException>(() => router.Handle<int, int>("x", n => n));
        Assert.Contains("already registered", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Handle_EmptyChannel_Throws(string? channel)
    {
        var (router, _) = CreateRouter();
        Assert.ThrowsAny<ArgumentException>(() => router.Handle<int, int>(channel!, n => n));
    }

    [Fact]
    public void Handle_NullHandler_Throws_AsyncOverload()
    {
        var (router, _) = CreateRouter();
        Assert.Throws<ArgumentNullException>(() => router.Handle<int, int>("x", (Func<int, Task<int>>)null!));
    }

    [Fact]
    public void Handle_NullHandler_Throws_SyncOverload()
    {
        var (router, _) = CreateRouter();
        Assert.Throws<ArgumentNullException>(() => router.Handle<int, int>("x", (Func<int, int>)null!));
    }

    [Fact]
    public void Remove_UnregistersHandler()
    {
        var (router, _) = CreateRouter();
        router.Handle<int, int>("x", n => n);

        Assert.True(router.Remove("x"));
        Assert.False(router.Remove("x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Remove_EmptyChannel_Throws(string? channel)
    {
        var (router, _) = CreateRouter();
        Assert.ThrowsAny<ArgumentException>(() => router.Remove(channel!));
    }

    [Fact]
    public async Task Invoke_AfterRemove_RejectsAsUnknown()
    {
        var (router, transport) = CreateRouter();
        router.Handle<int, int>("x", n => n);
        router.Remove("x");

        transport.Receive(InvokeFrame(5, "x", 1));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.False(result.Ok);
    }

    // ── Error / edge dispatch paths ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_UnknownChannel_RejectsWithError()
    {
        var (_, transport) = CreateRouter();

        transport.Receive(InvokeFrame(3, "missing", 1));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.Equal(3, result.Seq);
        Assert.False(result.Ok);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public async Task Invoke_HandlerThrows_RejectsGenerically_AndReportsToCallback()
    {
        Exception? reported = null;
        string? reportedChannel = null;
        var (router, transport) = CreateRouter((ch, ex) => { reportedChannel = ch; reported = ex; });
        router.Handle<int, int>("boom", (Func<int, int>)(_ => throw new InvalidOperationException("kaboom")));

        transport.Receive(InvokeFrame(9, "boom", 1));

        IpcMessage result = Parse(await transport.WaitForNextAsync());
        Assert.False(result.Ok);
        // The renderer must NOT receive the internal exception text.
        Assert.DoesNotContain("kaboom", result.Error);
        // The diagnostics callback DOES receive the specifics.
        Assert.Equal("boom", reportedChannel);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal("kaboom", reported!.Message);
    }

    [Fact]
    public async Task MalformedJson_IsDropped_NoResponse()
    {
        var (_, transport) = CreateRouter();

        transport.Receive("{ this is not json ");

        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public async Task NonInvokeInbound_Result_IsIgnored()
    {
        var (_, transport) = CreateRouter();

        var stray = new IpcMessage { Kind = IpcMessageKind.Result, Seq = 1, Ok = true };
        transport.Receive(JsonSerializer.Serialize(stray, IpcJsonContext.Default.IpcMessage));

        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public async Task NonInvokeInbound_Event_IsIgnored()
    {
        var (_, transport) = CreateRouter();

        var stray = new IpcMessage { Kind = IpcMessageKind.Event, Channel = "c" };
        transport.Receive(JsonSerializer.Serialize(stray, IpcJsonContext.Default.IpcMessage));

        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public async Task Invoke_NullChannel_IsIgnored()
    {
        var (_, transport) = CreateRouter();

        transport.Receive(InvokeFrame(4, channel: null, payload: 1));

        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public async Task Invoke_NoSeq_RegisteredHandler_PostsNothing()
    {
        var (router, transport) = CreateRouter();
        router.Handle<int, int>("x", n => n);

        // A notification (seq omitted) expects no reply even though the handler runs.
        transport.Receive(InvokeFrame(seq: null, "x", 1));

        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public async Task Invoke_NoSeq_UnknownChannel_PostsNothing()
    {
        var (_, transport) = CreateRouter();

        transport.Receive(InvokeFrame(seq: null, "missing", 1));

        await transport.AssertNoneSentAsync();
    }

    // ── Emit ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Emit_SendsEventFrame()
    {
        var (router, transport) = CreateRouter();

        router.Emit("tick", 99);

        Assert.Single(transport.Sent);
        IpcMessage evt = Parse(transport.Sent[0]);
        Assert.Equal(IpcMessageKind.Event, evt.Kind);
        Assert.Equal("tick", evt.Channel);
        Assert.Equal(99, evt.Payload!.Value.Deserialize<int>(Options));
        Assert.Null(evt.Seq);
    }

    [Fact]
    public void Emit_NullPayload_SendsEventWithNullPayload()
    {
        var (router, transport) = CreateRouter();

        router.Emit<string?>("tick", null);

        Assert.Single(transport.Sent);
        IpcMessage evt = Parse(transport.Sent[0]);
        Assert.Equal(IpcMessageKind.Event, evt.Kind);
        Assert.Null(evt.Payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Emit_EmptyChannel_Throws(string? channel)
    {
        var (router, _) = CreateRouter();
        Assert.ThrowsAny<ArgumentException>(() => router.Emit(channel!, 1));
    }

    // ── Dispose ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_UnsubscribesFromTransport()
    {
        var (router, transport) = CreateRouter();
        router.Handle<int, int>("x", n => n);
        router.Dispose();

        // After dispose the router no longer observes inbound frames.
        transport.Receive(InvokeFrame(1, "x", 1));
        await transport.AssertNoneSentAsync();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var (router, _) = CreateRouter();
        router.Dispose();
        router.Dispose();
    }

    // ── Concurrency ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handle_ConcurrentRegistration_IsThreadSafe()
    {
        var (router, _) = CreateRouter();

        Parallel.For(0, 200, i => router.Handle<int, int>($"ch{i}", n => n));

        // All registered, and a duplicate still throws (registry integrity intact).
        Assert.Throws<ArgumentException>(() => router.Handle<int, int>("ch0", n => n));
        for (int i = 0; i < 200; i++)
        {
            Assert.True(router.Remove($"ch{i}"));
        }
    }
}