using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Positron;
using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Tests the reserved <c>positron.*</c> built-in channels (file dialogs + text file I/O) that back
/// <c>window.positron.dialog</c> / <c>window.positron.fs</c>. Drives them through a real
/// <see cref="IpcRouter"/> + <see cref="FakeWebViewBackend"/>, exactly as the web content would.
/// </summary>
public sealed class BuiltinChannelsTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "positron-fs-" + Guid.NewGuid().ToString("N") + ".txt");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    // The router needs both the app's (here: empty) resolver and the library built-ins resolver,
    // mirroring what App.Run composes.
    private static JsonSerializerOptions BuiltinOptions() =>
        new() { TypeInfoResolver = JsonTypeInfoResolver.Combine(PositronBuiltinsContext.Default) };

    private static (Window Window, FakeWebViewBackend Backend, IpcRouter Router) CreateWiredWindow(
        Action<string, Exception>? onError = null)
    {
        var backend = new FakeWebViewBackend();
        var router = new IpcRouter(backend, BuiltinOptions(), onError);
        var window = new Window(backend, router);
        window.RegisterBuiltins(backend);
        return (window, backend, router);
    }

    private static string Invoke(long seq, string channel, string payloadJson) =>
        $"{{\"kind\":\"invoke\",\"seq\":{seq},\"channel\":\"{channel}\",\"payload\":{payloadJson}}}";

    private static async Task<IpcMessage> WaitResult(FakeWebViewBackend backend)
    {
        for (int i = 0; i < 200 && backend.Posted.Count == 0; i++)
        {
            await Task.Delay(5);
        }

        Assert.NotEmpty(backend.Posted);
        return JsonSerializer.Deserialize(backend.Posted[0], IpcJsonContext.Default.IpcMessage)!;
    }

    [Fact]
    public async Task DialogOpen_ReturnsBackendPath()
    {
        var (_, backend, _) = CreateWiredWindow();
        backend.NextDialogResult = "/tmp/picked.txt";

        backend.Receive(Invoke(1, "positron.dialog.open", "{}"));

        IpcMessage result = await WaitResult(backend);
        Assert.True(result.Ok);
        Assert.Equal("/tmp/picked.txt", result.Payload!.Value.GetString());
    }

    [Fact]
    public async Task DialogSave_Cancelled_ReturnsNull()
    {
        var (_, backend, _) = CreateWiredWindow();
        backend.NextDialogResult = null;

        backend.Receive(Invoke(2, "positron.dialog.save", "{}"));

        IpcMessage result = await WaitResult(backend);
        Assert.True(result.Ok);
        // A null handler result is omitted from the envelope (WhenWritingNull).
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task FsWriteThenRead_RoundTrips()
    {
        Exception? err = null;
        var (_, backend, _) = CreateWiredWindow((_, e) => err = e);

        // write
        string writePayload = JsonSerializer.Serialize(
            new WriteTextRequest { Path = _tempFile, Contents = "hello positron" },
            PositronBuiltinsContext.Default.WriteTextRequest);
        backend.Receive(Invoke(3, "positron.fs.writeText", writePayload));
        IpcMessage writeResult = await WaitResult(backend);
        Assert.True(writeResult.Ok, err?.ToString() ?? "write failed");
        Assert.Equal("hello positron", await File.ReadAllTextAsync(_tempFile));

        // read (fresh backend so Posted[0] is the read result)
        var (_, backend2, _) = CreateWiredWindow();
        string readPayload = JsonSerializer.Serialize(
            new ReadTextRequest { Path = _tempFile }, PositronBuiltinsContext.Default.ReadTextRequest);
        backend2.Receive(Invoke(4, "positron.fs.readText", readPayload));
        IpcMessage readResult = await WaitResult(backend2);
        Assert.True(readResult.Ok);
        Assert.Equal("hello positron", readResult.Payload!.Value.GetString());
    }
}