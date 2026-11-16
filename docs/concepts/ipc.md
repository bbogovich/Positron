# IPC

Positron connects C# and JavaScript through a small IPC layer: an internal `IpcRouter`
sitting on top of an `IMessageTransport` (the native backend). A preload script — injected at
document-start on every backend — defines `window.positron` in the web content. The design
mirrors Electron's `contextBridge`: a narrow, validated surface, never a raw native handle.

## The wire envelope

Every message crossing the boundary, in both directions, is a single JSON envelope:

```json
{ "kind": "invoke" | "result" | "event", "seq": 1, "channel": "greet", "payload": {}, "ok": true, "error": null }
```

The `kind` field is serialized **lowercase**. Each kind populates only the subset of fields it
needs:

| `kind` | Direction | Fields used |
|--------|-----------|-------------|
| `invoke` | JS → C# | `seq`, `channel`, `payload` |
| `result` | C# → JS | `seq`, `ok`, and either `payload` or `error` |
| `event` | C# → JS | `channel`, `payload` |

- **`invoke`** is a request/response correlated by `seq` — a monotonic call id the JS side
  assigns and the C# side echoes back on the matching `result`, so the right pending Promise
  resolves.
- **`result`** carries `ok: true` with a `payload`, or `ok: false` with an `error` string.
- **`event`** is a fire-and-forget push from C# to JS listeners; it has no `seq`.

## C# side: registering handlers

Register handlers on the [`Window`](../api/window.md) inside the `App.Run` configure callback.
Both synchronous and async handlers are supported:

```csharp
// Synchronous
window.Handle<GreetRequest, GreetResponse>("greet",
    req => new GreetResponse($"Hello, {req.Name}!"));

// Asynchronous
window.Handle<SaveRequest, bool>("save", async req =>
{
    await File.WriteAllTextAsync(req.Path, req.Contents);
    return true;
});
```

Argument and result types are (de)serialized using the source-generated JSON context you pass
to [`App.Run`](../api/app.md). Declare every payload type `[JsonSerializable]` on that context.

Push an event to the page with `Emit`:

```csharp
window.Emit("tick", new Tick(DateTimeOffset.Now.ToString("HH:mm:ss")));
```

## JavaScript side: `window.positron`

The preload bridge installs a frozen `window.positron` object (it never double-installs, and
is not writable or reconfigurable):

### `invoke(channel, payload) → Promise`

Calls a C# channel handler and resolves with its result. Rejects if the handler throws or the
channel is invalid.

```js
const res = await window.positron.invoke("greet", { name: "world" });
console.log(res.message);
```

### `on(channel, cb) → () => void`

Subscribes to events pushed from C# on a channel. Returns an **unsubscribe** function.

```js
const off = window.positron.on("tick", (payload) => console.log("tick", payload.time));
// later:
off();
```

## Built-in namespaces

The host registers reserved `positron.*` channels so apps get file dialogs and text-file I/O
without writing their own handlers. The bridge exposes them as convenience namespaces over
`invoke`:

| JS call | Resolves to |
|---------|-------------|
| `positron.dialog.open(options)` | the chosen path, or `null` if cancelled |
| `positron.dialog.save(options)` | the chosen path, or `null` if cancelled |
| `positron.fs.readText(path)` | the file's UTF-8 contents |
| `positron.fs.writeText(path, contents)` | completes when written |

```js
const path = await window.positron.dialog.open({ allowedExtensions: ["txt", "md"] });
if (path) {
  const text = await window.positron.fs.readText(path);
}
```

Dialog options mirror the C# [file-dialog option types](../api/file-dialogs.md):
`title`, `allowedExtensions` (no leading dot), `initialDirectory`, and — for save —
`suggestedFileName`.

## How the seams connect

The bridge is backend-agnostic. The native host wires two seams around the injected script:

- `window.__positronPostToNative(json)` — set by the backend; sends a JS → C# message.
- `window.__positronReceive(json)` — defined by the bridge; the backend calls it to deliver a
  C# → JS `result` or `event`.

A faulty event listener is isolated — one throwing callback does not break delivery to the
others. See [Architecture](architecture.md) for the full round-trip sequence.
