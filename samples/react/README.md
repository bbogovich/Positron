# Positron + React (Vite) sample

A minimal React app (Vite, `react-ts` template) whose UI calls a C# handler through the Positron
bridge. The C# host (`../react-host`) serves the production build over `app://localhost/`.

## Layout

- `samples/react/` — the React app. `src/positronService.ts` wraps `window.positron.invoke`;
  `src/App.tsx` calls it. `src/positron.d.ts` types the injected `window.positron`.
- `samples/react-host/` — the Positron C# host. Sets `WindowOptions.ServeDirectory` to
  `samples/react/dist` and loads `app://localhost/`.

## Build and run

```bash
# 1. Build the web app (outputs to samples/react/dist)
cd samples/react
npm install
npm run build

# 2. Run the Positron host (serves dist/ over app://localhost/)
cd ../..
dotnet run --project samples/react-host
```

During web-app development you can also run `npm run dev` for Vite's dev server, but the IPC
bridge (`window.positron`) only exists inside the Positron window — `greet` will reject in a plain
browser tab.

## app:// serving notes

Positron serves `dist/` with a real origin (`app://localhost`) and history-API fallback to
`index.html`. The default Vite base (`base: "/"`, set explicitly in `vite.config.ts`) is correct:
assets are requested as absolute `/assets/...` paths against that origin. Do **not** switch to a
relative base (`base: "./"`) — that is only needed for a `file://` origin, which Positron does not use.
