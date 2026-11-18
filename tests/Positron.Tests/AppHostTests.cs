using Positron;
using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Tests for the small, platform-agnostic pieces of the public host surface that do not need a
/// native window: <see cref="App.BuildPreload"/>, the embedded <c>BridgeScript</c> loader, and
/// <see cref="WindowOptions"/> defaults.
/// </summary>
public sealed class AppHostTests
{
    [Fact]
    public void BuildPreload_NullAppPreload_IsBridgeOnly()
    {
        string preload = App.BuildPreload(null);
        Assert.Equal(BridgeScript.Source, preload);
    }

    [Fact]
    public void BuildPreload_WithAppPreload_AppendsAfterBridge()
    {
        const string app = "window.foo = 1;";
        string preload = App.BuildPreload(app);
        Assert.Equal(BridgeScript.Source + "\n" + app, preload);
    }

    [Fact]
    public void BridgeScript_Source_IsLoadable_AndDefinesPositron()
    {
        string source = BridgeScript.Source;
        Assert.False(string.IsNullOrWhiteSpace(source));
        Assert.Contains("window.positron", source);
        Assert.Contains("__positronReceive", source);
    }

    [Fact]
    public void WindowOptions_Defaults()
    {
        var options = new WindowOptions();
        Assert.Equal("Positron", options.Title);
        Assert.Equal(1024, options.Width);
        Assert.Equal(768, options.Height);
        Assert.True(options.Resizable);
        Assert.Null(options.Preload);
        Assert.Equal(ContextMenuMode.Default, options.ContextMenu);
        Assert.False(options.EnableDevTools);
        Assert.Null(options.ServeDirectory);
        Assert.Equal("index.html", options.ServeEntry);
    }
}
/// <summary>Host wiring in <see cref="App.RunWith"/> that needs a backend (uses the fake).</summary>
public sealed partial class AppRunWiringTests
{
    private sealed record Arg(string Value);

    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(Arg))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(string))]
    private sealed partial class WiringJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }

    private const string InvokeBoom = """{"kind":"invoke","seq":1,"channel":"boom","payload":{"value":"x"}}""";

    [Fact]
    public void HandlerException_IsReportedToHandlerError_AndPageGetsGenericRejection()
    {
        var backend = new FakeWebViewBackend();
        string? reportedChannel = null;
        Exception? reported = null;

        App.RunWith(backend, new WindowOptions(), window =>
        {
            window.HandlerError += (channel, ex) => { reportedChannel = channel; reported = ex; };
            window.Handle<Arg, string>("boom", (Func<Arg, string>)(_ => throw new InvalidOperationException("secret detail")));
            backend.Receive(InvokeBoom);
        }, WiringJsonContext.Default.Options);

        Assert.Equal("boom", reportedChannel);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Single(backend.Posted);
        Assert.Contains("\"ok\":false", backend.Posted[0]);
        Assert.DoesNotContain("secret detail", backend.Posted[0]);
    }

    [Fact]
    public void PayloadDeserializationFailure_IsReportedToHandlerError()
    {
        var backend = new FakeWebViewBackend();
        Exception? reported = null;

        App.RunWith(backend, new WindowOptions(), window =>
        {
            window.HandlerError += (_, ex) => reported = ex;
            window.Handle<Arg, string>("boom", a => a.Value);
            // An array cannot be deserialized into the Arg record.
            backend.Receive("""{"kind":"invoke","seq":1,"channel":"boom","payload":[1,2]}""");
        }, WiringJsonContext.Default.Options);

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(reported);
    }

    [Fact]
    public void ThrowingHandlerErrorSubscriber_DoesNotBreakDispatch()
    {
        var backend = new FakeWebViewBackend();

        App.RunWith(backend, new WindowOptions(), window =>
        {
            window.HandlerError += (_, _) => throw new InvalidOperationException("subscriber bug");
            window.Handle<Arg, string>("boom", (Func<Arg, string>)(_ => throw new InvalidOperationException("x")));
            backend.Receive(InvokeBoom);
        }, WiringJsonContext.Default.Options);

        Assert.Single(backend.Posted); // the rejection was still delivered
    }

    [Fact]
    public void UserClose_RaisesWindowClosed()
    {
        var backend = new FakeWebViewBackend();
        bool closed = false;

        App.RunWith(backend, new WindowOptions(), window =>
        {
            window.Closed += () => closed = true;
        }, WiringJsonContext.Default.Options);

        // Simulate the native window being closed by the user (not via Window.Close()).
        backend.Close();
        Assert.True(closed);
    }
}

/// <summary>Tests that redirect the process-wide <see cref="Console.Error"/>; never run in parallel.</summary>
[CollectionDefinition(nameof(ConsoleRedirectCollection), DisableParallelization = true)]
public sealed class ConsoleRedirectCollection
{
}

[Collection(nameof(ConsoleRedirectCollection))]
public sealed partial class AppRunStderrTests
{
    private sealed record Arg(string Value);

    [System.Text.Json.Serialization.JsonSerializable(typeof(Arg))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(string))]
    private sealed partial class StderrJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }

    [Fact]
    public void HandlerException_WithNoSubscriber_IsWrittenToStderr()
    {
        var backend = new FakeWebViewBackend();
        TextWriter original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            App.RunWith(backend, new WindowOptions(), window =>
            {
                window.Handle<Arg, string>("boom", (Func<Arg, string>)(_ => throw new InvalidOperationException("visible detail")));
                backend.Receive("""{"kind":"invoke","seq":1,"channel":"boom","payload":{"Value":"x"}}""");
            }, StderrJsonContext.Default.Options);
        }
        finally
        {
            Console.SetError(original);
        }

        string output = captured.ToString();
        Assert.Contains("channel 'boom'", output);
        Assert.Contains("visible detail", output);
    }
}