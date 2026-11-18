# Window

`sealed class Positron.Window`

A live Positron window. It wraps the native backend and the IPC router, exposing a small
surface for loading content, registering C# handlers callable from JavaScript, pushing events
to the web content, and showing native file dialogs. You obtain one via the `configure`
callback of [`App.Run`](app.md) — you do not construct it yourself.

## Loading content

### `LoadUrl`

```csharp
public void LoadUrl(string url)
```

Navigates the web content to a URL — e.g. a `file://` or `http://` address, or `app://localhost/`
when [`WindowOptions.ServeDirectory`](window-options.md) is set. Throws if `url` is null or empty.

Loading a URL also **trusts its origin**: only documents from origins the host loaded (through
`LoadUrl`, `LoadHtml`, or `ServeDirectory`) can use the IPC bridge. See
[Security](../concepts/security.md#only-documents-you-loaded-can-use-the-bridge).

```csharp
window.LoadUrl(new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html")).AbsoluteUri);
```

### `LoadHtml`

```csharp
public void LoadHtml(string html)
```

Loads an HTML string directly into the web content. Throws if `html` is null.

```csharp
window.LoadHtml("<!doctype html><h1>Hello</h1>");
```

## IPC handlers

### `Handle` (synchronous)

```csharp
public void Handle<TArg, TResult>(string channel, Func<TArg, TResult> handler)
```

Registers a synchronous C# handler invokable from JavaScript via
`window.positron.invoke(channel, payload)`. `TArg` is deserialized from the invoke payload;
the returned `TResult` is serialized back as the result.

### `Handle` (asynchronous)

```csharp
public void Handle<TArg, TResult>(string channel, Func<TArg, Task<TResult>> handler)
```

The async overload — the handler returns a `Task<TResult>`. Use it for I/O-bound work.

```csharp
window.Handle<GreetRequest, GreetResponse>("greet", req => new GreetResponse($"Hi, {req.Name}!"));

window.Handle<SaveRequest, bool>("save", async req =>
{
    await File.WriteAllTextAsync(req.Path, req.Contents);
    return true;
});
```

Both `TArg` and `TResult` must be declared `[JsonSerializable]` on the source-generated JSON
context passed to [`App.Run`](app.md). See [IPC](../concepts/ipc.md) and
[Security](../concepts/security.md) (validate handler arguments).

## Events

### `Emit`

```csharp
public void Emit<TPayload>(string channel, TPayload payload)
```

Pushes an event to JavaScript listeners registered via `window.positron.on(channel, cb)`.
Fire-and-forget; there is no response.

```csharp
window.Emit("tick", new Tick(DateTimeOffset.Now.ToString("HH:mm:ss")));
```

## Lifecycle

### `Close`

```csharp
public void Close()
```

Closes the window and exits the native run loop, unblocking [`App.Run`](app.md).
[`Closed`](#closed) is raised once the native window has closed. Safe to call from a handler or
any thread.

### `Closed`

```csharp
public event Action? Closed
```

Raised once, on the UI thread, when the window closes — whether the user closed it (title-bar
close button, Cmd-W, the window manager) or `Close` was called. `App.Run` returns right after.
Use it to release app resources (timers, file handles) tied to the window's lifetime.

```csharp
var timer = new Timer(_ => window.Emit("tick", DateTime.Now), null, 0, 1000);
window.Closed += () => timer.Dispose();
```

## Errors

### `HandlerError`

```csharp
public event Action<string, Exception>? HandlerError
```

Raised when a C# handler throws — including when the JavaScript payload cannot be deserialized
into the handler's argument type. The arguments are the channel name and the exception. The page
only ever receives a generic rejection, so this is where the real failure should be logged. If
nothing is subscribed, the failure is written to standard error. Raised on the thread the handler
faulted on, often a thread-pool thread.

```csharp
window.HandlerError += (channel, ex) => logger.LogError(ex, "IPC handler {Channel} failed", channel);
```

## Threading

`App.Run` and the `configure` callback run on the main (UI) thread. Every other `Window` member —
`LoadUrl`, `LoadHtml`, `Emit`, `Close`, the file dialogs — may be called from any thread (async
handler continuations and timers run on the thread pool); Positron marshals the native work onto
the UI thread.

## Native file dialogs

### `ShowOpenFileAsync`

```csharp
public Task<string?> ShowOpenFileAsync(Files.OpenFileOptions? options = null)
```

Shows a native open-file dialog on the UI thread and returns the selected absolute path, or
`null` if the user cancelled. Read the file with `System.IO.File` from the host. See
[`OpenFileOptions`](file-dialogs.md).

### `ShowSaveFileAsync`

```csharp
public Task<string?> ShowSaveFileAsync(Files.SaveFileOptions? options = null)
```

Shows a native save-file dialog and returns the chosen absolute path, or `null` if cancelled.
Write the file with `System.IO.File` from the host. See [`SaveFileOptions`](file-dialogs.md).

```csharp
string? path = await window.ShowOpenFileAsync(
    new Positron.Files.OpenFileOptions { AllowedExtensions = ["txt", "md"] });
if (path is not null)
{
    string text = await File.ReadAllTextAsync(path);
}
```

!!! note
    The renderer can reach equivalent functionality through the built-in
    `window.positron.dialog.*` / `window.positron.fs.*` channels — see [IPC](../concepts/ipc.md).
    These `Window` methods are the C# host-side API for the same dialogs.
