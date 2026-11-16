# Positron

**Cross-platform .NET host for HTML/JS user interfaces running in the OS-native WebView —
a lightweight alternative to Electron with no bundled Chromium or Node.js.**

Positron hosts your web UI in the WebView already built into each operating system — WKWebView
on macOS, WebView2 (Edge/Chromium) on Windows, WebKitGTK on Linux — and gives you a typed
C#↔JavaScript IPC bridge, window/app lifecycle, and a project template. There is **no bundled
browser engine**: distributables stay small and the dependency/supply-chain surface stays tiny.

| | Electron | Positron |
|---|---|---|
| Rendering engine | Bundled Chromium (per app) | OS-native WebView (shared) |
| Runtime | Bundled Node.js | .NET (your app) |
| Typical bundle size | 100–250 MB+ | A few MB + your assets |
| Render consistency | Identical everywhere | Native per-OS (WebKit/Chromium) |

The trade-off is deliberate: you accept minor per-platform rendering differences in exchange
for a fraction of the size and dependency footprint. See [the rationale](#why).

## Status

Early development. The IPC core and the **macOS** backend are implemented and verified
end-to-end. The **Windows** (WebView2) and **Linux** (WebKitGTK) backends are implemented and
compile, but have not yet been run on their target platforms.

## Quick start

```csharp
using Positron;

App.Run(new WindowOptions { Title = "Hello", Width = 800, Height = 600 }, window =>
{
    // JavaScript calls window.positron.invoke("greet", { name }) → this handler runs.
    window.Handle<GreetRequest, GreetResponse>("greet", req => new GreetResponse($"Hi, {req.Name}!"));

    // Push events to the page: window.positron.on("tick", cb)
    window.Emit("tick", DateTime.Now);

    window.LoadHtml("<!doctype html>…");   // or window.LoadUrl("file:///…/index.html")
}, AppJsonContext.Default.Options);         // source-gen context → trim/AOT-safe
```

In the web content:

```js
const res = await window.positron.invoke("greet", { name: "world" });
console.log(res.message);                   // "Hi, world!"

const off = window.positron.on("tick", (t) => console.log("tick", t));
// off();  // unsubscribe
```

### Scaffold a new app

```bash
dotnet new install Positron.Templates      # once
dotnet new positron-app -n MyApp
```

## How it works

```
 Your C# app  ─▶  Positron.App / Window  ─▶  IpcRouter  ─▶  IWebViewBackend
                                                             │  (selected by OS)
                                   ┌─────────────────────────┼─────────────────────────┐
                                 macOS                     Windows                    Linux
                              WKWebView                  WebView2                  WebKitGTK
                            (libobjc P/Invoke)        (Win32 + Core)            (GObject P/Invoke)
```

- **IPC contract** — a seq-correlated request/response model (like the `webview/webview` C
  library). `invoke` returns a Promise; `on` subscribes to C#-pushed events. A preload script
  (`window.positron`) is injected at document start — a small, validated surface, never a raw
  native handle (the Electron contextBridge model).
- **Single assembly, pure P/Invoke** — no bundled native binaries; Positron calls the system
  WebView libraries directly and selects the backend at runtime via `OperatingSystem.Is*()`.
- **Trim/AOT-ready** — `[LibraryImport]` source-generated interop and `System.Text.Json`
  source generation throughout.

## Per-platform runtime prerequisites

| OS | Requirement |
|----|-------------|
| macOS | None — WebKit.framework ships with the OS. |
| Windows | The [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (present on Windows 11; install on older Windows). |
| Linux | `libwebkit2gtk-4.1` (e.g. `libwebkit2gtk-4.1-0` / `-dev` on Debian/Ubuntu). |

## Why

Electron ships a full Chromium and Node.js runtime in every application. That makes bundles
very large and pulls in a wide dependency tree — a maintenance burden and a supply-chain risk.
When a consistent, web-based UI is the goal and pixel-identical cross-engine rendering is not a
hard requirement, the OS-native WebView delivers the same developer experience at a fraction of
the size, with the browser engine maintained and patched by the OS vendor.

## Repository layout

```
src/Positron/                 the library (NuGet package)
tests/Positron.Tests/         unit tests — IPC core, no window (runs on all OSes)
tests/Positron.IntegrationTests/  E2E — launches the sample as a child process (macOS)
samples/Positron.Sample.Hello/    runnable sample + E2E target (`--selftest`)
templates/positron-app/       `dotnet new positron-app` template
```

## Building

The .NET 11 SDK is required.

```bash
dotnet build
dotnet test --filter "Category!=E2E"    # unit tests, all platforms
dotnet test --filter "Category=E2E"     # end-to-end (macOS; Linux under xvfb)
```

## License

[MIT](LICENSE)
