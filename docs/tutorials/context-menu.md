# Tutorial: context menus

By default a Positron window shows the WebView's built-in context menu (the OS/engine default). You
control it with two `WindowOptions`:

- `ContextMenu` — `ContextMenuMode.Default` (show it) or `ContextMenuMode.Disabled` (suppress it).
- `EnableDevTools` — `true` to allow the WebView's developer tools (right-click → Inspect, where the
  platform supports it). Leave `false` in release builds.

```csharp
var options = new WindowOptions
{
    Title = "My App",
    ContextMenu = ContextMenuMode.Disabled, // no default right-click menu
    EnableDevTools = true,                  // dev tools during development
};
```

## Providing your own menu

A custom context menu is ordinary web UI: disable the default menu in the host, then handle the
`contextmenu` event in the page. This is exactly what the [text editor](text-editor.md) does — a
one-item menu with a "Save" action:

```js
const menu = document.getElementById("menu"); // a styled, hidden <div> of buttons

document.addEventListener("contextmenu", (e) => {
  e.preventDefault();                 // stop the (already-disabled) default
  menu.style.left = e.clientX + "px";
  menu.style.top = e.clientY + "px";
  menu.style.display = "block";
});

// Dismiss on any left click or Escape.
document.addEventListener("click", () => { menu.style.display = "none"; });
document.addEventListener("keydown", (e) => { if (e.key === "Escape") menu.style.display = "none"; });

document.getElementById("menu-save").addEventListener("click", () => {
  menu.style.display = "none";
  save(); // your action — may call window.positron.invoke(...) to reach C#
});
```

```html
<div id="menu">
  <button id="menu-save">Save</button>
  <!-- add more items here -->
</div>
```

Because the menu is HTML, you style it with CSS (the sample uses the `Canvas`/`CanvasText`/
`Highlight` system colors so it matches light and dark mode automatically) and its actions are just
JavaScript — including `window.positron.invoke(...)` calls into your C# handlers.

!!! tip "Why an in-page menu rather than a native one?"
    An HTML menu is consistent across macOS/Windows/Linux, styleable, and needs no extra API. For a
    native application menu bar, that's a separate (future) capability; for right-click menus, the
    in-page approach is the simplest thing that works everywhere.

## Developer tools

During development, set `EnableDevTools = true` and right-click → Inspect (macOS/Windows). This is
the WebView's real inspector — DOM, console, network, and sources — so you debug the renderer
exactly as you would a web page. Turn it off for release builds.

## Next steps

- [Text editor](text-editor.md) — the custom menu in a complete app.
- [Security](../concepts/security.md) — why disabling the default menu is common for app-like UIs.
