# Positron

**A lightweight .NET alternative to Electron.** Positron hosts your HTML/JS user interface
in the WebView already built into each operating system and gives you a typed
C#↔JavaScript IPC bridge, window and app lifecycle, and a project template — with **no
bundled browser engine**.

## What it is

Positron is a single, RID-agnostic managed .NET assembly. It P/Invokes the *system* WebView
on each platform:

- **macOS** — WKWebView (via the Objective-C runtime)
- **Windows** — WebView2 (Edge/Chromium, over a Win32 window)
- **Linux** — WebKitGTK (via GObject)

Your web content runs in that native WebView; your application logic runs in .NET. A small,
validated IPC bridge connects the two — modelled on Electron's `contextBridge`: a narrow,
typed surface, never a raw native handle.

## Why — the trade-off

Electron ships a full copy of Chromium and Node.js *inside every app*. That makes bundles
large (hundreds of megabytes) and gives each app its own browser engine to keep patched — a
real supply-chain and update surface.

Positron takes the opposite bet. It uses the WebView the OS already ships and keeps patched,
so a distributable is **a few megabytes plus your assets**. The cost is deliberate: because
each OS has a different engine, you accept minor per-platform rendering differences instead of
one identical engine everywhere.

| | Electron | Positron |
|---|---|---|
| Rendering engine | Bundled Chromium (per app) | OS-native WebView (shared) |
| App runtime | Bundled Node.js | .NET (your app) |
| Typical bundle size | 100–250 MB+ | A few MB + your assets |
| Render consistency | Identical everywhere | Native per-OS (WebKit / Chromium) |
| Engine patching | You ship updates | The OS keeps it patched |

!!! note "Project status"
    Positron is in early development. The IPC core and the macOS backend are implemented and
    verified end-to-end. The Windows (WebView2) and Linux (WebKitGTK) backends are implemented
    and compile, but have not yet been exercised on their target platforms.

## Quickstart

```csharp
using System.Text.Json.Serialization;
using Positron;

var options = new WindowOptions { Title = "Hello", Width = 800, Height = 600 };

App.Run(options, window =>
{
    // JavaScript calls window.positron.invoke("greet", { name }) → this handler runs.
    window.Handle<GreetRequest, GreetResponse>("greet",
        req => new GreetResponse($"Hi, {req.Name}!"));

    window.LoadHtml("<!doctype html><button onclick=\"greet()\">Greet</button>");
}, AppJsonContext.Default.Options); // source-gen context → trim/AOT-safe

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

```js
// In the web content:
const res = await window.positron.invoke("greet", { name: "world" });
console.log(res.message); // "Hi, world!"
```

## Where to go next

- **[Getting Started](getting-started.md)** — prerequisites, scaffold a project, run it.
- **Concepts** — [Architecture](concepts/architecture.md), [IPC](concepts/ipc.md),
  [Security](concepts/security.md).
- **[Tutorials](tutorials/web-ui.md)** — building UIs, frameworks, file access, context menus.
- **API Reference** — [App](api/app.md), [Window](api/window.md),
  [WindowOptions](api/window-options.md), [File Dialogs](api/file-dialogs.md).
- **[Packaging](packaging/index.md)** — ship per-OS installers.
- **[Contributing](contributing.md)** — build and develop Positron itself.
