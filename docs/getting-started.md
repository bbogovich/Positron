# Getting Started

This guide takes you from a clean machine to a running Positron window.

## Prerequisites

Positron targets **.NET 11**. Install the [.NET 11 SDK](https://dotnet.microsoft.com/download)
for your platform.

Positron P/Invokes the *system* WebView, so the only extra requirement is that the OS WebView
runtime is present:

| OS | Required runtime |
|----|------------------|
| **macOS** | None — WKWebView ships with the OS. |
| **Windows** | The [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). It is preinstalled on current Windows; install it if missing. |
| **Linux** | WebKitGTK — install the `libwebkit2gtk-4.1` package (e.g. `sudo apt install libwebkit2gtk-4.1-0` on Debian/Ubuntu). |

Positron ships **no native binaries of its own** — it calls the libraries the OS already
provides.

## Scaffold a new app

Install the project template once, then create a project from it:

```bash
dotnet new install Positron.Templates   # once per machine
dotnet new positron-app -n MyApp
cd MyApp
dotnet run
```

A window opens showing the bundled `wwwroot/index.html`, already wired to a C# `greet`
handler over the IPC bridge.

## The minimal program

A Positron app is a normal .NET console project that calls `App.Run`. Here is the smallest
useful program — a C# handler, a source-generated JSON context, and some content to load:

```csharp
using System.Text.Json.Serialization;
using Positron;

var options = new WindowOptions
{
    Title = "MyApp",
    Width = 1024,
    Height = 768,
};

App.Run(options, window =>
{
    // JS → C#: window.positron.invoke("greet", { name })
    window.Handle<GreetRequest, GreetResponse>("greet",
        req => new GreetResponse($"Hello, {req.Name}!"));

    window.LoadHtml("""
        <!doctype html>
        <meta charset="utf-8">
        <body>
          <input id="name" value="world">
          <button id="go">Greet</button>
          <p id="out"></p>
          <script>
            document.getElementById("go").onclick = async () => {
              const res = await window.positron.invoke("greet",
                { name: document.getElementById("name").value });
              document.getElementById("out").textContent = res.message;
            };
          </script>
        </body>
        """);
}, AppJsonContext.Default.Options);

/// <summary>Request payload for the <c>greet</c> channel.</summary>
internal sealed record GreetRequest(string Name);

/// <summary>Response payload for the <c>greet</c> channel.</summary>
internal sealed record GreetResponse(string Message);

/// <summary>Source-generated JSON context — keeps the app trim- and NativeAOT-safe.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

### Why the JSON context is required

The third argument to [`App.Run`](api/app.md) — `serializerOptions` — is **not optional**.
Pass a source-generated `JsonSerializerContext.Default.Options`. This is what keeps a Positron
app trim- and NativeAOT-compatible: it exists precisely so an AOT build can never silently
fall back to reflection-based serialization and break at runtime. Every IPC payload type
(request and response) must be declared `[JsonSerializable]` on that context.

## Loading content: inline, file, or a served directory

- **`window.LoadHtml(string)`** — load an HTML string directly (shown above).
- **`window.LoadUrl(string)`** — navigate to a `file://` or `http://` URL. The template uses
  this to load a bundled `wwwroot/index.html`:

    ```csharp
    string indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
    window.LoadUrl(new Uri(indexPath).AbsoluteUri);
    ```

- **`ServeDirectory`** — for a built SPA (Angular, React, …), set
  [`WindowOptions.ServeDirectory`](api/window-options.md) and load `app://localhost/`. This
  serves the folder over the `app://` scheme with a real web origin, correct MIME types, and
  single-page-application history fallback:

    ```csharp
    var options = new WindowOptions { ServeDirectory = "dist" };
    App.Run(options, window => window.LoadUrl("app://localhost/"),
        AppJsonContext.Default.Options);
    ```

## Next steps

- Understand how it fits together in [Architecture](concepts/architecture.md).
- Learn the message model in [IPC](concepts/ipc.md).
- Read the [Security](concepts/security.md) model before exposing handlers.
