using System.Text.Json;
using System.Text.Json.Serialization;

using Positron;
using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Tests that <see cref="Window"/> correctly delegates to its backend and router, and enforces its
/// argument guards. Uses a <see cref="FakeWebViewBackend"/> so no native window is required.
/// </summary>
public sealed partial class WindowTests
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(int))]
    private sealed partial class WindowJsonContext : JsonSerializerContext
    {
    }

    private static (Window Window, FakeWebViewBackend Backend) CreateWindow()
    {
        var backend = new FakeWebViewBackend();
        var router = new IpcRouter(backend, new JsonSerializerOptions { TypeInfoResolver = WindowJsonContext.Default });
        var window = new Window(backend, router);
        return (window, backend);
    }

    [Fact]
    public void LoadUrl_DelegatesToBackend()
    {
        var (window, backend) = CreateWindow();
        window.LoadUrl("https://example.test/");
        Assert.Equal("https://example.test/", backend.NavigatedUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void LoadUrl_EmptyOrNull_Throws(string? url)
    {
        var (window, _) = CreateWindow();
        Assert.ThrowsAny<ArgumentException>(() => window.LoadUrl(url!));
    }

    [Fact]
    public void LoadHtml_DelegatesToBackend()
    {
        var (window, backend) = CreateWindow();
        window.LoadHtml("<h1>hi</h1>");
        Assert.Equal("<h1>hi</h1>", backend.LoadedHtml);
    }

    [Fact]
    public void LoadHtml_Null_Throws()
    {
        var (window, _) = CreateWindow();
        Assert.Throws<ArgumentNullException>(() => window.LoadHtml(null!));
    }

    [Fact]
    public void Emit_PostsEventThroughBackend()
    {
        var (window, backend) = CreateWindow();
        window.Emit("tick", 7);

        Assert.Single(backend.Posted);
        IpcMessage evt = JsonSerializer.Deserialize(backend.Posted[0], IpcJsonContext.Default.IpcMessage)!;
        Assert.Equal(IpcMessageKind.Event, evt.Kind);
        Assert.Equal("tick", evt.Channel);
    }

    [Fact]
    public async Task Handle_RoutesInboundInvokeThroughWindow()
    {
        var (window, backend) = CreateWindow();
        window.Handle<int, int>("inc", n => n + 1);

        var frame = new IpcMessage
        {
            Kind = IpcMessageKind.Invoke,
            Seq = 1,
            Channel = "inc",
            Payload = JsonSerializer.SerializeToElement(41, WindowJsonContext.Default.Int32),
        };
        backend.Receive(JsonSerializer.Serialize(frame, IpcJsonContext.Default.IpcMessage));

        // Poll briefly for the async-void dispatch to post a result.
        for (int i = 0; i < 100 && backend.Posted.Count == 0; i++)
        {
            await Task.Delay(5);
        }

        Assert.Single(backend.Posted);
        IpcMessage result = JsonSerializer.Deserialize(backend.Posted[0], IpcJsonContext.Default.IpcMessage)!;
        Assert.True(result.Ok);
        Assert.Equal(42, result.Payload!.Value.Deserialize<int>());
    }

    [Fact]
    public void Close_DelegatesToBackend_AndRaisesClosed()
    {
        var (window, backend) = CreateWindow();
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;

        window.Close();

        Assert.True(backend.Closed);
        Assert.True(closedRaised);
    }
}