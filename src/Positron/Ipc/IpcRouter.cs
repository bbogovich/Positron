using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Positron.Ipc;

/// <summary>
/// Routes messages between the web content and C# handlers over an <see cref="IMessageTransport"/>.
/// This is the pure-logic heart of Positron's IPC: it has no knowledge of any window or native
/// API, so it is fully unit-testable with an in-memory transport.
/// </summary>
/// <remarks>
/// Threading: inbound dispatch runs on whatever thread the transport raises
/// <see cref="IMessageTransport.MessageReceived"/> on. Handlers may be async. Registration is
/// thread-safe. Each backend is responsible for ensuring <see cref="IMessageTransport.PostMessage"/>
/// ultimately delivers on its UI thread.
/// </remarks>
internal sealed class IpcRouter : IDisposable
{
    private readonly IMessageTransport _transport;
    private readonly JsonSerializerOptions _options;
    private readonly Action<string, Exception>? _onHandlerError;
    private readonly ConcurrentDictionary<string, Func<JsonElement, Task<object?>>> _handlers = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Creates a router bound to a transport.
    /// </summary>
    /// <param name="transport">The message carrier (real WebView bridge or a test fake).</param>
    /// <param name="options">
    /// Serializer options for handler argument and result types. Supply a
    /// <see cref="JsonSerializerOptions"/> whose <see cref="JsonSerializerOptions.TypeInfoResolver"/>
    /// is a source-generated context to stay trim- and AOT-safe.
    /// </param>
    /// <param name="onHandlerError">
    /// Optional diagnostics callback invoked (channel, exception) when a handler faults. The web
    /// content only ever receives a generic error string; the specific exception is surfaced here
    /// for server-side logging and never crosses to the renderer.
    /// </param>
    public IpcRouter(IMessageTransport transport, JsonSerializerOptions options, Action<string, Exception>? onHandlerError = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        _transport = transport;
        _options = options;
        _onHandlerError = onHandlerError;
        _transport.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Registers a request/response handler for a channel. The handler receives the deserialized
    /// request argument and returns a value that is serialized back to the awaiting JS Promise.
    /// </summary>
    /// <typeparam name="TArg">The request argument type deserialized from the JS payload.</typeparam>
    /// <typeparam name="TResult">The result type serialized back to JS.</typeparam>
    /// <param name="channel">The channel name. Must be unique and non-empty.</param>
    /// <param name="handler">The async handler.</param>
    /// <exception cref="ArgumentException">The channel name is empty or already registered.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TArg is resolved through the caller-supplied JsonSerializerOptions; AOT callers pass a source-generated resolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "TArg is resolved through the caller-supplied JsonSerializerOptions; AOT callers pass a source-generated resolver.")]
    public void Handle<TArg, TResult>(string channel, Func<TArg, Task<TResult>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(handler);

        Func<JsonElement, Task<object?>> wrapper = async payload =>
        {
            TArg arg = payload.Deserialize<TArg>(_options)!;
            TResult result = await handler(arg).ConfigureAwait(false);
            return result;
        };

        if (!_handlers.TryAdd(channel, wrapper))
        {
            throw new ArgumentException($"A handler is already registered for channel '{channel}'.", nameof(channel));
        }
    }

    /// <summary>
    /// Registers a synchronous request/response handler for a channel. Convenience overload
    /// around <see cref="Handle{TArg, TResult}(string, Func{TArg, Task{TResult}})"/>.
    /// </summary>
    public void Handle<TArg, TResult>(string channel, Func<TArg, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Handle<TArg, TResult>(channel, arg => Task.FromResult(handler(arg)));
    }

    /// <summary>
    /// Removes a previously registered handler.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>True if a handler was removed; false if none was registered.</returns>
    public bool Remove(string channel)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        return _handlers.TryRemove(channel, out _);
    }

    /// <summary>
    /// Pushes a fire-and-forget event to the web content. JavaScript listeners registered via
    /// <c>window.positron.on(channel, cb)</c> receive the payload.
    /// </summary>
    /// <typeparam name="TPayload">The payload type, serialized to JSON.</typeparam>
    /// <param name="channel">The event channel name.</param>
    /// <param name="payload">The event payload.</param>
    public void Emit<TPayload>(string channel, TPayload payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        var message = new IpcMessage
        {
            Kind = IpcMessageKind.Event,
            Channel = channel,
            Payload = Box(payload),
        };
        _transport.PostMessage(Serialize(message));
    }

    /// <summary>
    /// Handles one inbound raw JSON message from the web content. Public to allow tests and
    /// backends to feed messages directly; normally invoked via the transport event.
    /// </summary>
    /// <param name="json">The raw inbound JSON string.</param>
    public async void Dispatch(string json)
    {
        // async void is deliberate: this is a top-level event-loop entry point with no caller to
        // await it. All faults are converted into Result envelopes or swallowed below so the
        // method never throws into the native message pump.
        IpcMessage? inbound;
        try
        {
            inbound = Deserialize(json);
        }
        catch (JsonException)
        {
            // A malformed frame has no seq to correlate a rejection to — drop it.
            return;
        }

        if (inbound is null || inbound.Kind != IpcMessageKind.Invoke || inbound.Channel is null)
        {
            return;
        }

        long? seq = inbound.Seq;

        if (!_handlers.TryGetValue(inbound.Channel, out var handler))
        {
            Reject(seq, $"No handler registered for channel '{inbound.Channel}'.");
            return;
        }

        try
        {
            JsonElement payload = inbound.Payload ?? default;
            object? result = await handler(payload).ConfigureAwait(false);
            Resolve(seq, result);
        }
        catch (Exception ex)
        {
            // Never leak internal exception text to the (untrusted) web content — surface the
            // specifics to the optional diagnostics callback and return a generic rejection.
            _onHandlerError?.Invoke(inbound.Channel, ex);
            Reject(seq, "The handler failed to process the request.");
        }
    }

    private void OnMessageReceived(string json) => Dispatch(json);

    private void Resolve(long? seq, object? result)
    {
        if (seq is null)
        {
            return; // A notification with no seq expects no reply.
        }

        var message = new IpcMessage
        {
            Kind = IpcMessageKind.Result,
            Seq = seq,
            Ok = true,
            Payload = Box(result),
        };
        _transport.PostMessage(Serialize(message));
    }

    private void Reject(long? seq, string error)
    {
        if (seq is null)
        {
            return;
        }

        var message = new IpcMessage
        {
            Kind = IpcMessageKind.Result,
            Seq = seq,
            Ok = false,
            Error = error,
        };
        _transport.PostMessage(Serialize(message));
    }

    /// <summary>
    /// Serializes an arbitrary value to a <see cref="JsonElement"/> for the payload slot, using
    /// the caller-provided options so application types resolve through their own context.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Caller supplies a source-generated resolver via JsonSerializerOptions for AOT/trim scenarios.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Caller supplies a source-generated resolver via JsonSerializerOptions for AOT/trim scenarios.")]
    private JsonElement? Box(object? value)
    {
        if (value is null)
        {
            return null;
        }

        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), _options);
        using var doc = JsonDocument.Parse(utf8);
        return doc.RootElement.Clone();
    }

    private static string Serialize(IpcMessage message) =>
        JsonSerializer.Serialize(message, IpcJsonContext.Default.IpcMessage);

    private static IpcMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.IpcMessage);

    /// <summary>Unsubscribes from the transport. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.MessageReceived -= OnMessageReceived;
    }
}