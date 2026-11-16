# Packaging for Windows

Produces a self-contained publish folder and a distributable `.zip`.

Script: [`scripts/packaging/windows-publish.ps1`](https://github.com/bbogovich/Positron/tree/main/scripts/packaging/windows-publish.ps1)

## What it does

1. `dotnet publish -r win-x64 -c Release --self-contained` — a folder of files with the .NET
   runtime bundled (no .NET install needed on the target machine).
2. Verifies **`WebView2Loader.dll`** is present in the output.
3. Zips the folder into `artifacts/dist/<AppName>-<Version>-win-x64.zip`.

A commented single-file variant and a commented `signtool` section are included for when you
need them.

## WebView2: Evergreen vs Fixed Version

Positron renders through **WebView2** (Edge/Chromium). Two runtime distribution modes:

- **Evergreen Runtime** (recommended default): installed and auto-updated by the OS. It is
  **preinstalled on Windows 11**; on older Windows it may be missing. Your app should detect it
  at runtime and, if absent, run the ~2 MB bootstrapper (`MicrosoftEdgeWebview2Setup.exe`). The
  canonical detection call is `CoreWebView2Environment.GetAvailableBrowserVersionString()` — a
  `null`/throw means "not installed". Download the bootstrapper from the
  [WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/).
- **Fixed Version Runtime**: a versioned runtime folder you redistribute *with* the app and
  point WebView2 at explicitly. Larger (~150 MB) but removes the install/update dependency —
  the right choice for offline or locked-down deployments.

Either way, the native loader shim **`WebView2Loader.dll` must ship next to the executable** —
it is what Positron's P/Invoke binds to in order to find and start the runtime. The script
warns if it is missing from the publish output; it is normally supplied by the
`Microsoft.Web.WebView2` package (copy it from the SDK's `runtimes/win-x64/native` folder if
not). See
[WebView2 distribution](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).

## Prerequisites

- Windows with PowerShell 5.1+ (or PowerShell 7).
- .NET 11 SDK (`dotnet` on `PATH`, or installed at `%ProgramFiles%\dotnet`).

## Usage

```powershell
cd scripts\packaging
./windows-publish.ps1
```

Override for your own app:

```powershell
./windows-publish.ps1 -AppName MyApp -Version 1.2.3 `
  -Project ..\..\src\MyApp\MyApp.csproj
```

Variables can also come from the environment (`APP_NAME`, `PROJECT`, `VERSION`, `PUBLISH_DIR`,
`DIST_DIR`), matching the other platform scripts.

## Single-file variant

The script's default is a multi-file folder publish — simplest to sign and it keeps
`WebView2Loader.dll` visible as a sibling. A single-file publish is available (commented):

```powershell
dotnet publish ... -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract=true` is **mandatory** for single-file here: native
libraries such as `WebView2Loader.dll` cannot be loaded from inside the bundle, so they must
extract to disk at startup, or WebView2 fails to initialize. See
[single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview).

## Code signing

Authenticode signing is commented in the script. Two facts that trip people up:

- **SHA-256 is mandatory.** `signtool` now requires `/fd SHA256` (file digest) and `/td SHA256`
  (timestamp digest); SHA-1 is rejected. Always include a timestamp server (`/tr`) so the
  signature survives certificate expiry.
- **Keys must live in hardware.** As of 2023+, CA/Browser Forum rules require OV and EV
  code-signing private keys to live on an **HSM / hardware token** or a cloud signing service
  (e.g. **Azure Trusted Signing** via `signtool`'s `/dlib` provider). A plain `.pfx` on disk
  only works for self-signed / internal certificates.

```powershell
# Hardware-token / cert-store certificate:
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /n "Your Company, Inc." "$PublishDir\MyApp.exe"
```

Re-zip **after** signing so the distributed archive contains the signed binary. See
[signtool](https://learn.microsoft.com/windows/win32/seccrypto/signtool).

## Format tradeoffs: folder/zip vs MSIX vs Velopack

- **Folder + `.zip`** (this script): zero extra tooling, trivial to sign, works everywhere.
  No install/uninstall integration or auto-update. Best default for a simple distribution.
- **MSIX**: modern Windows installer with clean install/uninstall and Store eligibility, but it
  requires signing even for sideloading and adds packaging complexity. Choose it when you need
  Store distribution or managed enterprise deployment.
- **Velopack / Squirrel**: third-party frameworks that add delta auto-updates and a friendly
  installer on top of a publish folder. Choose when in-app auto-update is a priority and you do
  not want MSIX.

## References

- [.NET application publishing](https://learn.microsoft.com/dotnet/core/deploying/)
- [WebView2 distribution](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)
- [signtool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
