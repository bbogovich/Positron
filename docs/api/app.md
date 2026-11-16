# App

`static class Positron.App`

The entry point for a Positron application. `App.Run` creates the native window and WebView
for the host OS, wires the IPC bridge, lets the caller register handlers and load content, then
enters the native run loop and blocks until the window closes.

## `App.Run`

```csharp
public static void Run(
    WindowOptions options,
    Action<Window> configure,
    JsonSerializerOptions serializerOptions)
```

Runs a Positron application. It builds the window, exposes `window.positron` to the web
content, invokes `configure` with the ready [`Window`](window.md), then **blocks in the native
run loop** until the window closes.

### Parameters

| Parameter | Meaning |
|-----------|---------|
| `options` | The window configuration — see [`WindowOptions`](window-options.md). |
| `configure` | Callback invoked with the ready `Window` *before* the run loop starts. Register IPC handlers (`Window.Handle`) and load content (`Window.LoadUrl` / `Window.LoadHtml`) here. |
| `serializerOptions` | **Required.** JSON options for IPC handler argument and result types. Pass a source-generated context's `Options` property (e.g. `MyJsonContext.Default.Options`). |

All three arguments are validated as non-null and throw `ArgumentNullException` otherwise.

### Why `serializerOptions` is required

Passing a source-generated `JsonSerializerContext.Default.Options` is what keeps a Positron app
trim- and NativeAOT-safe — that is the point of Positron's small-footprint model. The parameter
is mandatory *precisely so* an AOT build cannot silently fall back to reflection-based
serialization and break at runtime. Declare every IPC payload type `[JsonSerializable]` on your
context. Positron internally combines your resolver with its own built-in-channel context, so
the reserved `positron.*` payloads also serialize correctly under trimming/AOT.

### Threading

`App.Run` must be called from the process **main thread**. On macOS AppKit requires it; on
Windows the backend establishes an STA message pump on the calling thread. The call does not
return until the window closes.

### Example

```csharp
using System.Text.Json.Serialization;
using Positron;

App.Run(
    new WindowOptions { Title = "MyApp", Width = 1024, Height = 768 },
    window =>
    {
        window.Handle<GreetRequest, GreetResponse>("greet",
            req => new GreetResponse($"Hello, {req.Name}!"));
        window.LoadUrl("app://localhost/");
    },
    AppJsonContext.Default.Options);

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

## See also

- [`Window`](window.md) — the object passed to `configure`.
- [`WindowOptions`](window-options.md) — the first argument.
- [IPC](../concepts/ipc.md) — the message model behind `Handle` / `Emit`.
