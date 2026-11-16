# Tutorial: a plain web UI

The simplest Positron app is a C# host plus a folder of static web files. There are two ways to get
your HTML into the window; this tutorial covers both and explains when to use each.

## Option A — serve a folder over `app://` (recommended)

Point `WindowOptions.ServeDirectory` at a folder and load `app://localhost/`. The folder is served
with a **real web origin**: absolute `/asset` paths work, `fetch`/`import` work, and unknown routes
fall back to `index.html` (so client-side routing works). This is the right choice for anything
beyond a single inline page, and the only correct choice for SPA frameworks
([Angular](angular.md), [React](react.md)).

```csharp
using System.Text.Json.Serialization;
using Positron;

var options = new WindowOptions
{
    Title = "My App",
    Width = 900,
    Height = 600,
    ServeDirectory = AppContext.BaseDirectory + "wwwroot", // ServeEntry defaults to index.html
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req => new GreetResponse($"Hello, {req.Name}!"));
    window.LoadUrl("app://localhost/");
}, AppJsonContext.Default.Options);

return 0;

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext { }
```

Copy the folder next to the executable in your `.csproj`:

```xml
<ItemGroup>
  <Content Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src 'unsafe-inline'; script-src 'self';" />
  <title>My App</title>
</head>
<body>
  <input id="name" value="world" />
  <button id="greet">Greet</button>
  <div id="out"></div>
  <script src="/app.js"></script>
</body>
</html>
```

`wwwroot/app.js`:

```js
document.getElementById("greet").addEventListener("click", async () => {
  const name = document.getElementById("name").value;
  const res = await window.positron.invoke("greet", { name });
  document.getElementById("out").textContent = res.message;
});
```

## Option B — inline HTML with `LoadHtml`

For a one-file demo or a splash screen, pass HTML directly — no folder, no serving:

```csharp
App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req => new GreetResponse($"Hello, {req.Name}!"));
    window.LoadHtml("<!doctype html><button onclick=\"…\">Greet</button>…");
}, AppJsonContext.Default.Options);
```

`LoadHtml` is handy for tiny UIs (the bundled `Positron.Sample.Hello` uses it), but there's no origin
and no asset resolution — external `src`/`href` and absolute paths won't resolve. Use **Option A**
for anything real.

## Talking to C#

Both options expose the same bridge:

- `window.positron.invoke(channel, payload)` → a `Promise` resolved by your C# `Handle` handler.
- `window.positron.on(channel, cb)` → subscribe to events the host pushes with `window.Emit(...)`;
  returns an unsubscribe function.

See [IPC](../concepts/ipc.md) for the full contract, and [the file system](file-system.md) for the
built-in dialog/file bridges.

## Next steps

- [Web components](web-components.md) — the same idea with custom elements.
- [Text editor](text-editor.md) — a complete app with files and a context menu.
