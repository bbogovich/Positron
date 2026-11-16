# Contributing

Positron is MIT-licensed and welcomes issues and pull requests. This page covers developing
**Positron itself**; to build an *app* with Positron, start at [Getting Started](getting-started.md).

## Development setup

- Install the [.NET 11 SDK](https://dotnet.microsoft.com/download). The repo pins
  `11.0.100` with `latestFeature` roll-forward via `global.json`.
- Clone the repo and build:

    ```bash
    dotnet build
    ```

The solution (`Positron.slnx`) contains the library (`src/Positron`), the samples
(`samples/`), the project template (`templates/positron-app`), and the test projects
(`tests/Positron.Tests`, `tests/Positron.IntegrationTests`).

## Validation gates

Run the gates CI enforces before opening a pull request:

```bash
dotnet format --verify-no-changes     # style (fix with: dotnet format)
dotnet build -c Release               # must be warning-free — warnings are errors
dotnet test --filter "Category!=E2E"  # unit tests, all platforms
```

If you touched a native backend, also run the end-to-end check on that platform:

```bash
dotnet test --filter "Category=E2E"   # macOS directly; Linux via `xvfb-run`
```

## Conventions

- **Style** is defined by `.editorconfig` and enforced with `dotnet format`. There is no
  Prettier (that is a JavaScript tool). Do not reformat code outside the scope of your change.
- **Doc comments** are required on public — and non-trivial internal — types and members.
- **Tests** accompany new functionality. The IPC core is deliberately testable without a
  window through the `IMessageTransport` seam; prefer unit tests there and reserve E2E for the
  native bridge.
- **Native interop** uses source-generated `[LibraryImport]` and must stay trim/AOT-clean.
  Platform P/Invoke lives under `src/Positron/Backends/<Os>/`, guarded with
  `[SupportedOSPlatform]`.
- **Surgical changes.** Keep each PR focused; avoid opportunistic cleanup in unrelated files.

## Project layout

| Path | Contents |
|------|----------|
| `src/Positron/` | The library — the single managed assembly. |
| `src/Positron/Backends/<Os>/` | Per-OS WebView backends (Mac, Windows, Linux). |
| `src/Positron/Ipc/` | IPC router, wire envelope, and the embedded preload bridge. |
| `src/Positron/Serving/` | `app://` static-file resolver. |
| `src/Positron/Files/` | File-dialog option types. |
| `samples/` | Runnable example apps (also the E2E targets). |
| `templates/positron-app/` | The `dotnet new positron-app` template. |
| `tests/` | Unit and integration test projects. |

## Platform backends

Positron selects a backend at runtime per OS. If you add support or fix a bug on a platform you
cannot test locally, say so in the PR so a maintainer on that platform can verify it. See
[Architecture](concepts/architecture.md) for how the backends fit together.
