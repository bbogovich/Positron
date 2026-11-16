# WindowOptions

`sealed class Positron.WindowOptions`

Configuration for the application's main window, supplied as the first argument to
[`App.Run`](app.md). All properties have defaults; set only what you need.

## Properties

| Property | Type | Default | Meaning |
|----------|------|---------|---------|
| `Title` | `string` | `"Positron"` | The window title shown in the title bar. |
| `Width` | `int` | `1024` | Initial content width in logical pixels. |
| `Height` | `int` | `768` | Initial content height in logical pixels. |
| `Resizable` | `bool` | `true` | Whether the user can resize the window. |
| `Preload` | `string?` | `null` | Optional extra JavaScript injected at document-start, *after* the Positron bridge. Null injects only the bridge. |
| `ContextMenu` | `ContextMenuMode` | `Default` | Whether the WebView shows its default (browser) context menu. |
| `EnableDevTools` | `bool` | `false` | Whether to enable the WebView's developer tools (right-click → Inspect, where supported). |
| `ServeDirectory` | `string?` | `null` | When set, serves this directory over the `app://` scheme. Null disables serving. |
| `ServeEntry` | `string` | `"index.html"` | The SPA entry document returned for client-side routes under `ServeDirectory`. |

## Notes

### `Preload`

Use it to expose an application-specific preload surface. The Positron bridge is always
injected first; your preload runs after it, so `window.positron` is available to it.

### `ContextMenu`

```csharp
public enum ContextMenuMode { Default, Disabled }
```

- `Default` — show the WebView's default context menu.
- `Disabled` — suppress it. Common for app-like UIs that provide their own right-click
  handling in JavaScript. See the [context-menu tutorial](../tutorials/context-menu.md).

### `EnableDevTools`

Enable during development; leave off in release builds — see [Security](../concepts/security.md).

### `ServeDirectory` / `ServeEntry`

Setting `ServeDirectory` serves the folder over the `app://` scheme with a real web origin,
correct MIME types, and single-page-application history fallback (unknown routes resolve to
`ServeEntry`). This is the supported way to host a built SPA such as Angular or React — load it
with `window.LoadUrl("app://localhost/")`. See [Architecture](../concepts/architecture.md) for
the serving flow.

```csharp
var options = new WindowOptions
{
    Title = "MyApp",
    ServeDirectory = "dist",   // your built SPA output
    ServeEntry = "index.html", // the default
    ContextMenu = ContextMenuMode.Disabled,
    EnableDevTools = false,
};
```
