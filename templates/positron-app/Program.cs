using System.Text.Json.Serialization;
using Positron;

// A new Positron app. The C# host exposes typed handlers to the web UI over window.positron, then
// loads wwwroot/index.html. Replace the handler and the UI with your own.

var options = new WindowOptions
{
    Title = "PositronApp",
    Width = 1024,
    Height = 768,
};

App.Run(options, window =>
{
    // JS → C#: window.positron.invoke("greet", { name })
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}!"));

    // Load the bundled web UI. index.html is copied next to the executable at build time.
    string indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
    window.LoadUrl(new Uri(indexPath).AbsoluteUri);
}, AppJsonContext.Default.Options);

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Source-generated JSON context for the app's IPC types (keeps the app trim/AOT friendly).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
