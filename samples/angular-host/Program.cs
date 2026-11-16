using System.Text.Json.Serialization;

using Positron;

// Positron host for the Angular sample.
//
// It serves the Angular production build over app://localhost/ and registers the "greet" channel
// the Angular service invokes. The @angular/build:application builder writes to
// samples/angular/dist/positron-ng/browser. Build the web app first:
//     cd samples/angular && npm install && npx ng build
// then run this host:
//     dotnet run --project samples/angular-host

// Resolve samples/angular/dist/positron-ng/browser relative to this host project.
string distDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "angular", "dist", "positron-ng", "browser"));

var options = new WindowOptions
{
    Title = "Positron — Angular",
    Width = 760,
    Height = 520,
    ServeDirectory = distDir,
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C# (Angular sample)"));

    window.LoadUrl("app://localhost/");
}, AngularHostJsonContext.Default.Options);

return 0;

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Source-generated JSON context for the sample's IPC types.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AngularHostJsonContext : JsonSerializerContext
{
}
