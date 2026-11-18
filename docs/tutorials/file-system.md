# Tutorial: the file system

Positron mediates all filesystem access through the **C# host** — the web page never gets raw disk
access. You reach the filesystem two ways:

1. **Built-in bridges** for the common cases — native open/save dialogs and UTF‑8 text I/O — callable
   straight from JavaScript with no host code.
2. **Your own handlers** (`window.Handle`) when you need something the built-ins don't cover (binary
   files, directories, app-specific validation, etc.).

## Built-in dialogs and text I/O (from JavaScript)

These are injected on `window.positron` automatically; the host registers nothing.

```js
// Open: resolves to an absolute path, or null if the user cancels.
const path = await window.positron.dialog.open({
  title: "Open a document",            // optional
  allowedExtensions: ["txt", "md"],    // optional, no leading dot
  initialDirectory: "/Users/me/Docs",  // optional
});

// Save: resolves to the chosen path, or null if cancelled.
const target = await window.positron.dialog.save({
  suggestedFileName: "untitled.txt",
  allowedExtensions: ["txt"],
});

// Text I/O by absolute path (UTF-8).
const text = await window.positron.fs.readText(path);
await window.positron.fs.writeText(target, "new contents");
```

The [text editor tutorial](text-editor.md) is a complete app built from exactly these calls.

## The same dialogs from C#

If you'd rather drive the dialog from the host (e.g. in response to a menu or a timer), `Window`
exposes them directly:

```csharp
App.Run(options, async window =>  // configure may be async
{
    string? path = await window.ShowOpenFileAsync(new Positron.Files.OpenFileOptions
    {
        AllowedExtensions = new[] { "txt", "md" },
    });

    if (path is not null)
    {
        string text = await File.ReadAllTextAsync(path);
        // ... use it, or push it to the page with window.Emit(...)
    }
}, MyJsonContext.Default.Options);
```

`OpenFileOptions` and `SaveFileOptions` (namespace `Positron.Files`) carry `Title`,
`AllowedExtensions`, `InitialDirectory`, and — for save — `SuggestedFileName`. See the
[File Dialogs API](../api/file-dialogs.md).

## Writing your own file handler

When the built-ins aren't enough, register a handler and call it with `window.positron.invoke`.
Here's a "read any file as base64" operation, which the built-in text API doesn't provide:

```csharp
// C# host
window.Handle<ReadRequest, string>("readBinaryBase64", async req =>
{
    byte[] bytes = await File.ReadAllBytesAsync(req.Path);
    return Convert.ToBase64String(bytes);
});

internal sealed record ReadRequest(string Path);
```

```js
// renderer
const b64 = await window.positron.invoke("readBinaryBase64", { path });
```

Remember to add `ReadRequest` (and any result types) to your `[JsonSerializable]` context.

## Security notes

- **Validate paths at the boundary.** A handler receives whatever the renderer sends. If an argument
  is a path, treat it as untrusted: constrain it to an expected root, reject traversal, etc. See
  [Security](../concepts/security.md).
- **The page can't read files on its own.** There is no `fs` in the renderer beyond the narrow,
  host-implemented `window.positron.fs` surface — exactly the point of the
  [contextBridge-style model](../concepts/security.md).
- **Handler exceptions don't leak.** If your handler throws (e.g. `FileNotFoundException`), the
  renderer gets a generic rejection; the real exception is raised on
  [`Window.HandlerError`](../api/window.md#handlererror) for logging (or written to standard error
  when nothing subscribes), not sent to the page.

## Next steps

- [Text editor](text-editor.md) — these APIs in a complete app.
- [IPC](../concepts/ipc.md) — the `invoke`/`Handle` contract behind both the built-ins and your own
  handlers.
