using System.Text.Json;
using System.Text.Json.Serialization;

namespace Positron.Ipc;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the IPC wire types. Using the
/// generator (rather than reflection-based serialization) keeps the library trim- and
/// NativeAOT-compatible, which is central to Positron's small-footprint goal.
/// </summary>
/// <remarks>
/// Handler argument and result types supplied by applications are serialized through the
/// caller-provided options in <see cref="IpcRouter"/>; this context covers only the fixed
/// envelope. <see cref="System.Text.Json.JsonElement"/> payloads pass through without needing
/// a generated contract.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcMessage))]
internal sealed partial class IpcJsonContext : JsonSerializerContext
{
}