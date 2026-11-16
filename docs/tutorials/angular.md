# Tutorial: an Angular app

Angular (and any SPA) runs in Positron by **serving its production build** over `app://localhost/`.
The `app://` origin is what makes this work: absolute `/asset` paths resolve, and unknown routes
fall back to `index.html`, so the Angular router behaves exactly as it does on the web.

The runnable source is in
[`samples/angular`](https://github.com/bbogovich/Positron/tree/main/samples/angular) (the Angular
app) and
[`samples/angular-host`](https://github.com/bbogovich/Positron/tree/main/samples/angular-host) (the
Positron host).

```bash
cd samples/angular && npm install && npx ng build   # build the SPA
dotnet run --project samples/angular-host            # serve + run it
```

## 1. Create the Angular app

Scaffold with the Angular CLI as usual:

```bash
npx @angular/cli@20 new positron-ng --routing --style css --ssr false
```

!!! warning "Angular CLI and Node version"
    The latest Angular CLI (v21+) requires Node `^22.22.3 || ^24.15.0 || >=26`. On an older Node 24
    (e.g. 24.13), pin **`@angular/cli@20`** as shown. Upgrade Node to use the newest CLI.

Leave the default `<base href="/">` in `index.html` — it is correct for the `app://localhost/`
origin. (Do **not** switch to a relative base; that's only for `file://`, which Positron does not
use.)

## 2. Declare and call the bridge

Add a type declaration so TypeScript knows about `window.positron` (`src/positron.d.ts`):

```ts
interface PositronApi {
  invoke<T = unknown>(channel: string, payload?: unknown): Promise<T>;
  on(channel: string, cb: (payload: unknown) => void): () => void;
}
declare global {
  interface Window { positron: PositronApi; }
}
export {};
```

Wrap IPC in a service (`src/app/positron.service.ts`):

```ts
import { Injectable } from "@angular/core";

@Injectable({ providedIn: "root" })
export class PositronService {
  greet(name: string): Promise<{ message: string }> {
    return window.positron.invoke("greet", { name });
  }
}
```

Use it from a component as you would any service. The build output lands in
`dist/positron-ng/browser` (the `@angular/build:application` builder nests it under `browser/`).

## 3. The Positron host

A tiny C# host points `ServeDirectory` at the Angular build and registers the `greet` channel:

```csharp
using System.Text.Json.Serialization;
using Positron;

// dist/positron-ng/browser relative to this host project.
string distDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "angular", "dist", "positron-ng", "browser"));

var options = new WindowOptions
{
    Title = "Positron — Angular",
    ServeDirectory = distDir,
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C#"));
    window.LoadUrl("app://localhost/");
}, AngularHostJsonContext.Default.Options);

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class AngularHostJsonContext : JsonSerializerContext { }
```

## Gotchas

- **Serve `dist/<project>/browser`, not `dist/<project>`.** The modern Angular builder adds the
  `browser/` subfolder. If you rename the project from `positron-ng`, update the host path.
- **Rebuild after web changes.** `ng build` must run before the host serves updated files. For a
  fast inner loop during development you can run `ng serve` and point `window.LoadUrl` at
  `http://localhost:4200/` instead, then switch back to `app://` serving for the shipped app.
- **Routing just works** because `app://` falls back to `index.html` for client-side routes.

## Next steps

- [React](react.md) — the same pattern with Vite.
- [Packaging](../packaging/index.md) — bundle the Angular host + its `dist` for distribution.
