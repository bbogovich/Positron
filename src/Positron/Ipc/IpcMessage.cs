using System.Text.Json;
using System.Text.Json.Serialization;

namespace Positron.Ipc;

/// <summary>
/// Discriminates the kind of message crossing the JS↔C# boundary. Serialized as the
/// lowercase <c>kind</c> field of every <see cref="IpcMessage"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IpcMessageKind>))]
internal enum IpcMessageKind
{
    /// <summary>A request from JavaScript invoking a registered C# channel handler.</summary>
    [JsonStringEnumMemberName("invoke")]
    Invoke,

    /// <summary>A response from C# resolving or rejecting a prior <see cref="Invoke"/>.</summary>
    [JsonStringEnumMemberName("result")]
    Result,

    /// <summary>A fire-and-forget event pushed from C# to JavaScript listeners.</summary>
    [JsonStringEnumMemberName("event")]
    Event,
}

/// <summary>
/// The single envelope exchanged over the WebView message bridge in both directions.
/// All fields are optional at the wire level because a given <see cref="IpcMessageKind"/>
/// only populates the subset it needs:
/// <list type="bullet">
///   <item><description><see cref="IpcMessageKind.Invoke"/>: <see cref="Seq"/>, <see cref="Channel"/>, <see cref="Payload"/>.</description></item>
///   <item><description><see cref="IpcMessageKind.Result"/>: <see cref="Seq"/>, <see cref="Ok"/>, and either <see cref="Payload"/> or <see cref="Error"/>.</description></item>
///   <item><description><see cref="IpcMessageKind.Event"/>: <see cref="Channel"/>, <see cref="Payload"/>.</description></item>
/// </list>
/// The contract mirrors the seq-correlated bind/return model used by the webview/webview C library.
/// </summary>
internal sealed class IpcMessage
{
    /// <summary>The message kind. Required on every message.</summary>
    [JsonPropertyName("kind")]
    public IpcMessageKind Kind { get; set; }

    /// <summary>
    /// Monotonic call id assigned by the JavaScript side, echoed back on the matching
    /// <see cref="IpcMessageKind.Result"/> so the pending Promise can be correlated.
    /// Null for events.
    /// </summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; set; }

    /// <summary>The channel name routing an invoke or carrying an event. Null on results.</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>
    /// The arbitrary JSON payload for the message, carried verbatim as a <see cref="JsonElement"/>
    /// so the router never has to know the concrete handler argument type until it deserializes it.
    /// </summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>On a result: true if the handler succeeded, false if it threw.</summary>
    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    /// <summary>On a failed result: the error message. Null on success.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}