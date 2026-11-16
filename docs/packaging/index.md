# Packaging

Positron apps are ordinary `dotnet publish` output: a windowed native executable that
P/Invokes the operating system's WebView (WKWebView on macOS, WebView2 on Windows,
WebKitGTK on Linux). There is **no bundled browser engine**, so packaging is mostly about
(1) producing a self-contained publish for the target OS and (2) wrapping it in the native
installable format users expect, while ensuring the OS WebView runtime is available.

This section documents the scripts under [`scripts/packaging/`](https://github.com/bbogovich/Positron/tree/main/scripts/packaging)
and the tradeoffs behind each platform's approach.

| Platform | Script | Produces | Guide |
|----------|--------|----------|-------|
| macOS | `macos-bundle.sh` + `Info.plist.template` + `entitlements.plist` | universal `.app` (optionally signed/notarized) | [macOS](macos.md) |
| Linux | `flatpak-build.sh` + `flatpak/` manifest set | `.flatpak` single-file bundle | [Linux](linux.md) |
| Windows | `windows-publish.ps1` | self-contained folder + `.zip` | [Windows](windows.md) |

All scripts take an example app name of `PositronApp` (the name produced by
`dotnet new positron-app`) with variables at the top you can override for your own app.

## The example app name

The `dotnet new positron-app` template creates `PositronApp.csproj`, so the published
apphost binary is named `PositronApp`. Every script defaults `APP_NAME=PositronApp` and
derives the apphost filename, bundle id, launcher name, etc. from it. Override `APP_NAME`
(and `PROJECT`, `BUNDLE_ID`, `VERSION`) to package your own app — for example to package the
repository sample whose assembly name is `Positron.Sample.Hello`:

```bash
APP_NAME=Positron.Sample.Hello \
PROJECT=samples/Positron.Sample.Hello/Positron.Sample.Hello.csproj \
scripts/packaging/macos-bundle.sh
```

## NativeAOT and why it matters for packaging

Positron's interop is `[LibraryImport]` source-generated and its JSON uses source generation,
so the library is **trim- and NativeAOT-ready**. Publishing with NativeAOT (`PublishAot=true`)
produces a single lean native binary with no JIT and no runtime code generation. That has a
direct packaging benefit on macOS: because nothing JITs, the hardened-runtime entitlements can
stay minimal — you can omit `com.apple.security.cs.allow-jit` and the unsigned-executable-memory
entitlement entirely (see [macOS](macos.md) and `scripts/packaging/entitlements.plist`). A smaller
entitlement set is a smaller attack surface and a smoother notarization.

NativeAOT cross-compilation has toolchain prerequisites (a native toolchain for each target),
so the macOS script exposes `POSITRON_AOT` (default on) which you can set to `0` to fall back to
a plain self-contained CoreCLR publish — at the cost of needing the JIT entitlement.

## WebView runtime per platform (summary)

| OS | Runtime | Bundled? |
|----|---------|----------|
| macOS | WebKit.framework (WKWebView) | No — ships with macOS. Nothing to bundle. |
| Windows | WebView2 Evergreen Runtime | No — preinstalled on Windows 11; detect + offer bootstrapper on older Windows. The native `WebView2Loader.dll` **must ship** next to the exe. |
| Linux | `libwebkit2gtk-4.1` | No — provided by the GNOME Flatpak runtime (`org.gnome.Platform//49`). |

See each platform guide for the details and the authoritative vendor documentation.
