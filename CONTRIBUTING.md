# Contributing to Positron

Thanks for your interest in contributing! Positron is MIT-licensed and welcomes issues and
pull requests.

## Development setup

- Install the [.NET 11 SDK](https://dotnet.microsoft.com/download).
- Clone the repo and run `dotnet build`.

## Before opening a pull request

Run the validation gates that CI enforces:

```bash
dotnet format --verify-no-changes     # style (fix with: dotnet format)
dotnet build -c Release               # must be warning-free (warnings are errors)
dotnet test --filter "Category!=E2E"  # unit tests, all platforms
```

If you touched a native backend, also run the end-to-end check on that platform:

```bash
dotnet test --filter "Category=E2E"   # macOS directly; Linux via `xvfb-run`
```

## Conventions

- **Style** is defined by `.editorconfig` and enforced with `dotnet format` — there is no
  Prettier (that is a JS tool). Do not reformat code outside the scope of your change.
- **Doc comments** are required on public (and non-trivial internal) types and members.
- **Tests** accompany new functionality. The IPC core is designed to be testable without a
  window via the `IMessageTransport` seam — prefer unit tests there; reserve E2E for the
  native bridge.
- **Native interop** uses `[LibraryImport]` (source-generated) and must stay trim/AOT-clean.
  Platform-specific P/Invoke lives under `src/Positron/Backends/<Os>/` guarded with
  `[SupportedOSPlatform]`.
- **Surgical changes.** Keep each PR focused; avoid opportunistic cleanup in unrelated files.

## Platform backends

Positron selects a backend at runtime. If you add support or fix a bug on a platform you can't
test locally, say so in the PR so a maintainer on that platform can verify.

The Windows (WebView2) backend compiles only on a Windows host by default. To compile-check it
from macOS or Linux:

```bash
dotnet build src/Positron -p:PositronForceWebView2=true -p:TreatWarningsAsErrors=false
```

(Off Windows the WebView2 package's WPF assembly raises an MSB3277 `WindowsBase` warning, hence
`TreatWarningsAsErrors=false`; the build is a compile check only.)
