# Positron + Angular sample

A minimal Angular app (Angular CLI, routing enabled) whose UI calls a C# handler through the
Positron bridge. The C# host (`../angular-host`) serves the production build over `app://localhost/`.

## Layout

- `samples/angular/` — the Angular app. `src/app/positron.service.ts` wraps `window.positron.invoke`;
  `src/app/app.ts` injects and calls it. `src/positron.d.ts` types the injected `window.positron`.
  Routing is enabled (`provideRouter`) so Positron's history-API fallback to `index.html` is exercised.
- `samples/angular-host/` — the Positron C# host. Sets `WindowOptions.ServeDirectory` to
  `samples/angular/dist/positron-ng/browser` and loads `app://localhost/`.

## Build and run

```bash
# 1. Build the Angular app (outputs to samples/angular/dist/positron-ng/browser)
cd samples/angular
npm install
npx ng build

# 2. Run the Positron host (serves the browser/ output over app://localhost/)
cd ../..
dotnet run --project samples/angular-host
```

During web-app development you can also run `npx ng serve`, but the IPC bridge
(`window.positron`) only exists inside the Positron window — `greet` will reject in a plain browser tab.

## app:// serving notes

Positron serves the build output with a real origin (`app://localhost`) and history-API fallback to
`index.html`. The Angular scaffold sets `<base href="/">` in `src/index.html`, which is exactly what
`app://localhost/` needs — assets resolve against the origin root. Leave it as `/`; do **not** change
it to `./` or a relative base.

The `@angular/build:application` builder (Angular 17+) nests the browser bundle under a `browser/`
subfolder, so the host points `ServeDirectory` at `dist/positron-ng/browser` (not just `dist/positron-ng`).
If you rename the Angular project, update that path in `angular-host/Program.cs` accordingly.
