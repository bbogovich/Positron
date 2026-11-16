using System.Text.Json.Serialization;

namespace Positron.Ipc;

/// <summary>Request payload for the <c>positron.dialog.open</c> built-in channel.</summary>
internal sealed class OpenDialogRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("allowedExtensions")]
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    [JsonPropertyName("initialDirectory")]
    public string? InitialDirectory { get; set; }
}

/// <summary>Request payload for the <c>positron.dialog.save</c> built-in channel.</summary>
internal sealed class SaveDialogRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("suggestedFileName")]
    public string? SuggestedFileName { get; set; }

    [JsonPropertyName("allowedExtensions")]
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    [JsonPropertyName("initialDirectory")]
    public string? InitialDirectory { get; set; }
}

/// <summary>Request payload for <c>positron.fs.readText</c>.</summary>
internal sealed class ReadTextRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>Request payload for <c>positron.fs.writeText</c>.</summary>
internal sealed class WriteTextRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("contents")]
    public string Contents { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated context for the reserved <c>positron.*</c> built-in channel payloads. It is
/// combined with the application's own resolver in <see cref="App"/> so both the built-ins and the
/// app's types serialize under trimming/NativeAOT.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenDialogRequest))]
[JsonSerializable(typeof(SaveDialogRequest))]
[JsonSerializable(typeof(ReadTextRequest))]
[JsonSerializable(typeof(WriteTextRequest))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal sealed partial class PositronBuiltinsContext : JsonSerializerContext
{
}