using System.Text.Json.Serialization;

using Positron;

// Positron host for the React + Vite sample.
//
// It serves the Vite production build (samples/react/dist) over app://localhost/ and registers the
// "greet" channel the React component invokes. Build the web app first:
//     cd samples/react && npm install && npm run build
// then run this host:
//     dotnet run --project samples/react-host

// Resolve samples/react/dist relative to this host project (works from `dotnet run` and the bin dir).
string distDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "react", "dist"));

var options = new WindowOptions
{
    Title = "Positron — React",
    Width = 760,
    Height = 520,
    ServeDirectory = distDir,
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C# (React sample)"));

    window.LoadUrl("app://localhost/");
}, ReactHostJsonContext.Default.Options);

return 0;

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Source-generated JSON context for the sample's IPC types.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class ReactHostJsonContext : JsonSerializerContext
{
}
