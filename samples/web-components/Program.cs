using System.Text.Json.Serialization;

using Positron;

// Positron web-components sample — a plain custom-elements app (no framework).
//
// The web UI defines a <positron-greeter> custom element that calls window.positron.invoke("greet")
// and renders the C# reply. The host serves the static wwwroot folder over app://localhost/ via
// WindowOptions.ServeDirectory and registers the single "greet" channel the element invokes.

var options = new WindowOptions
{
    Title = "Positron — Web Components",
    Width = 720,
    Height = 480,
    ServeDirectory = AppContext.BaseDirectory + "wwwroot",
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C#"));

    window.LoadUrl("app://localhost/");
}, WebComponentsJsonContext.Default.Options);

return 0;

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Source-generated JSON context for the sample's IPC types.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class WebComponentsJsonContext : JsonSerializerContext
{
}
