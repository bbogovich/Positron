using System.Text.Json;

using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Pins the on-the-wire JSON shape of <see cref="IpcMessage"/> / <see cref="IpcMessageKind"/>.
/// The enum values MUST serialize as lowercase (<c>invoke</c>/<c>result</c>/<c>event</c>) because
/// the JavaScript bridge compares against those literals — a regression to PascalCase would be
/// invisible to a C#-only round trip but would silently break every IPC reply in the browser.
/// </summary>
public sealed class IpcMessageWireFormatTests
{
    private static string Serialize(IpcMessage m) =>
        JsonSerializer.Serialize(m, IpcJsonContext.Default.IpcMessage);

    [Theory]
    [InlineData("Invoke", "\"kind\":\"invoke\"")]
    [InlineData("Result", "\"kind\":\"result\"")]
    [InlineData("Event", "\"kind\":\"event\"")]
    public void Kind_SerializesAsLowercaseString(string kindName, string expectedFragment)
    {
        var kind = Enum.Parse<IpcMessageKind>(kindName);
        string json = Serialize(new IpcMessage { Kind = kind });
        Assert.Contains(expectedFragment, json);
    }

    [Theory]
    [InlineData("invoke", "Invoke")]
    [InlineData("result", "Result")]
    [InlineData("event", "Event")]
    public void Kind_DeserializesFromLowercaseString(string wire, string expectedName)
    {
        IpcMessage? m = JsonSerializer.Deserialize(
            $"{{\"kind\":\"{wire}\"}}", IpcJsonContext.Default.IpcMessage);
        Assert.NotNull(m);
        Assert.Equal(Enum.Parse<IpcMessageKind>(expectedName), m!.Kind);
    }

    [Fact]
    public void NullProperties_AreOmitted()
    {
        // DefaultIgnoreCondition = WhenWritingNull keeps the envelope compact and lets the JS side
        // treat absent fields uniformly.
        string json = Serialize(new IpcMessage { Kind = IpcMessageKind.Event, Channel = "c" });
        Assert.DoesNotContain("\"seq\"", json);
        Assert.DoesNotContain("\"error\"", json);
        Assert.DoesNotContain("\"payload\"", json);
    }
}