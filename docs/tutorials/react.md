# Tutorial: a React app

React runs in Positron the same way every SPA does: **build it, serve the output over
`app://localhost/`**. This tutorial uses Vite (the current React default); Create React App or any
other bundler works identically — you just serve its `dist`/`build` folder.

The runnable source is in
[`samples/react`](https://github.com/bbogovich/Positron/tree/main/samples/react) (the Vite app) and
[`samples/react-host`](https://github.com/bbogovich/Positron/tree/main/samples/react-host) (the
Positron host).

```bash
cd samples/react && npm install && npm run build   # build the SPA → dist/
dotnet run --project samples/react-host             # serve + run it
```

## 1. Create the React app

```bash
npm create vite@latest positron-react -- --template react-ts
```

In `vite.config.ts`, keep (or set) **`base: "/"`** — the built `index.html` then references
`/assets/...`, which resolve against the `app://localhost/` origin:

```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/", // correct for app://; do NOT use "./" (that's for file:// origins)
});
```

## 2. Declare and call the bridge

Type declaration (`src/positron.d.ts`):

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

A small service (`src/positronService.ts`):

```ts
export function greet(name: string): Promise<{ message: string }> {
  return window.positron.invoke("greet", { name });
}
```

Use it from a component:

```tsx
import { useState } from "react";
import { greet } from "./positronService";

export default function App() {
  const [name, setName] = useState("world");
  const [msg, setMsg] = useState("");
  return (
    <>
      <input value={name} onChange={(e) => setName(e.target.value)} />
      <button onClick={async () => setMsg((await greet(name)).message)}>Greet via C#</button>
      <p>{msg}</p>
    </>
  );
}
```

## 3. The Positron host

Point `ServeDirectory` at Vite's `dist` and register the channel:

```csharp
using System.Text.Json.Serialization;
using Positron;

// samples/react/dist relative to this host project.
string distDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "react", "dist"));

var options = new WindowOptions
{
    Title = "Positron — React",
    ServeDirectory = distDir,
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C#"));
    window.LoadUrl("app://localhost/");
}, ReactHostJsonContext.Default.Options);

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class ReactHostJsonContext : JsonSerializerContext { }
```

## Gotchas

- **`base: "/"`, not `"./"`.** Positron serves over a real origin (`app://localhost/`), so absolute
  asset paths are correct. Relative bases are a `file://` workaround Positron doesn't need.
- **Rebuild after web changes**, or use Vite's dev server (`npm run dev`) and point
  `window.LoadUrl` at `http://localhost:5173/` during development, switching to `app://` for the
  shipped build.
- **Client-side routing works** — `app://` falls back to `index.html` for unknown routes, so
  React Router (or any history-API router) behaves normally.

## Next steps

- [Angular](angular.md) — the same pattern with the Angular CLI.
- [Packaging](../packaging/index.md) — ship the React host + its `dist`.
