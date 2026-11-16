using System.Text.Json.Serialization;

using Positron;
using Positron.Sample.Hello;

// Positron.Sample.Hello — a minimal windowed app that demonstrates the full round trip:
//   * a C# handler invoked from JavaScript (window.positron.invoke)
//   * a C# → JavaScript event stream (window.positron.on)
//   * the preload bridge exposing window.positron
//
// It doubles as the end-to-end test target and as the shape the dotnet-new template produces.
//
// Passing "--selftest" runs a bounded, headless round-trip check and exits 0 (pass) / 1 (fail)
// instead of entering the normal run loop. The integration test launches this mode as a separate
// process so native AppKit owns a real main thread (a unit-test host does not, and crashes).

if (args.Contains("--selftest-serve"))
{
    return SelfTest.Run("serve");
}

if (args.Contains("--selftest"))
{
    return SelfTest.Run();
}

var options = new WindowOptions
{
    Title = "Positron — Hello",
    Width = 820,
    Height = 560,
};

App.Run(options, window =>
{
    // JS → C#: window.positron.invoke("greet", { name: "..." })
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — from C# ({Environment.OSVersion.Platform})"));

    // C# → JS: push a tick event once a second; the page subscribes via window.positron.on.
    // The timer is held in a field-like closure and disposed when the window closes, so it is
    // rooted for the window's lifetime and torn down cleanly (not left to the GC).
    var timer = new Timer(_ => window.Emit("tick", new Tick(DateTimeOffset.Now.ToString("HH:mm:ss"))),
        state: null, dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));
    window.Closed += () => timer.Dispose();

    window.LoadHtml(SampleHtml.Document);
}, SampleJsonContext.Default.Options);

return 0;

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Payload for the <c>tick</c> event.</summary>
internal sealed record Tick(string Time);

/// <summary>Source-generated JSON context for the sample's IPC types (keeps it trim/AOT friendly).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
[JsonSerializable(typeof(Tick))]
internal sealed partial class SampleJsonContext : JsonSerializerContext
{
}