# Tutorial: a text editor

This is the canonical first "real" Positron app: a tiny text editor that **opens** a file from
disk, lets you **edit** it in the web UI, and **saves** it back. It ties together the three things
most desktop apps need — serving a web UI, native file dialogs, and file I/O — in about 40 lines of
C# and 60 lines of HTML/JS.

The complete, runnable source is in [`samples/Positron.Sample.TextEditor`](https://github.com/bbogovich/Positron/tree/main/samples/Positron.Sample.TextEditor).

```bash
dotnet run --project samples/Positron.Sample.TextEditor
```

## How it works

The app uses **only the built-in bridges** Positron injects into the page, so the C# host registers
no channels of its own:

- `window.positron.dialog.open(options)` / `.save(options)` — native open/save dialogs, resolving
  to an absolute path or `null` if cancelled.
- `window.positron.fs.readText(path)` / `.writeText(path, contents)` — UTF‑8 text file I/O,
  mediated by the C# host (the web page never gets raw filesystem access).

The UI is a static `wwwroot` folder served over `app://localhost/` — the recommended way to ship a
web UI (real origin, absolute `/asset` paths, history-API fallback). See
[Serving a web UI](web-ui.md) for why.

## The C# host

`Program.cs` is almost entirely configuration — serve the folder, disable the default context menu
(the page supplies its own), and load the app:

```csharp
using System.Text.Json.Serialization;
using Positron;

var options = new WindowOptions
{
    Title = "Positron — Text Editor",
    Width = 760,
    Height = 560,
    ServeDirectory = AppContext.BaseDirectory + "wwwroot", // ServeEntry defaults to index.html
    ContextMenu = ContextMenuMode.Disabled,                // page provides its own menu
};

App.Run(options, window =>
{
    window.LoadUrl("app://localhost/");
}, TextEditorJsonContext.Default.Options);

return 0;

// App.Run requires a source-generated JSON context (for trim/AOT safety). This app calls only the
// built-in channels, so it has no payload types of its own — but the generator needs at least one
// [JsonSerializable] to emit the context, so we register the primitive the built-ins use.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
internal sealed partial class TextEditorJsonContext : JsonSerializerContext { }
```

!!! note "Why a JSON context with no app types?"
    `App.Run` requires a `JsonSerializerContext` so your app stays trim- and NativeAOT-safe. An
    empty source-gen context won't compile (the generator emits nothing), so add at least one
    `[JsonSerializable(typeof(string))]`. The library's own built-in payloads are registered
    internally.

## The web UI

`wwwroot/index.html` is a header with **Open**/**Save** buttons, a `<textarea>`, and a one-item
custom context menu:

```html
<header>
  <button id="open">Open</button>
  <button id="save">Save</button>
  <span id="path">(no file)</span>
</header>
<textarea id="editor" spellcheck="false" placeholder="Open a .txt or .md file…"></textarea>
<div id="menu"><button id="menu-save">Save</button></div>
<script src="/app.js"></script>
```

`wwwroot/app.js` is the whole behavior — open, save, and the context menu:

```js
let currentPath = null; // absolute path of the current file, or null for an unsaved buffer

async function open() {
  const path = await window.positron.dialog.open({ allowedExtensions: ["txt", "md"] });
  if (!path) return;                                  // user cancelled
  editor.value = await window.positron.fs.readText(path);
  setPath(path);
}

async function save() {
  let path = currentPath;
  if (!path) {                                        // first save → ask where
    path = await window.positron.dialog.save({
      allowedExtensions: ["txt", "md"],
      suggestedFileName: "untitled.txt",
    });
    if (!path) return;                                // user cancelled
  }
  await window.positron.fs.writeText(path, editor.value);
  setPath(path);
}

document.getElementById("open").addEventListener("click", open);
document.getElementById("save").addEventListener("click", save);
```

The custom right-click menu — shown because the host set `ContextMenu = Disabled` — is a plain DOM
element toggled on the `contextmenu` event:

```js
const menu = document.getElementById("menu");
document.addEventListener("contextmenu", (e) => {
  e.preventDefault();
  menu.style.left = e.clientX + "px";
  menu.style.top = e.clientY + "px";
  menu.style.display = "block";
});
document.addEventListener("click", () => { menu.style.display = "none"; });
document.getElementById("menu-save").addEventListener("click", () => { menu.style.display = "none"; save(); });
```

## What to take away

- **File access lives in C#.** The dialogs and file I/O run in the host; the renderer only ever sees
  a path string and file contents it explicitly asked for. This is the same trust boundary Electron
  gets from `contextBridge`.
- **The UI is just the web.** Everything except the four `window.positron.*` calls is ordinary
  HTML/JS you could paste into any browser.
- **No channels to wire** for common file operations — the built-ins cover open/save/read/write. When
  you need app-specific operations, register your own with `window.Handle(...)`; see
  [the file-system tutorial](file-system.md) and [IPC](../concepts/ipc.md).

## Next steps

- [File system](file-system.md) — the dialog and `fs` APIs in depth, plus writing your own
  file-related handlers.
- [Context menus](context-menu.md) — the default menu, disabling it, dev tools, and custom menus.
- [Packaging](../packaging/index.md) — ship this editor as a macOS `.app`, a Linux Flatpak, or a
  Windows folder.
