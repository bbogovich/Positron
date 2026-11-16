#Requires -Version 5.1
<#
.SYNOPSIS
    Publish and package a Positron app for Windows (WebView2).

.DESCRIPTION
    Publishes the app self-contained for win-x64, verifies the WebView2 loader is present,
    and zips the output folder for distribution.

    Positron renders through WebView2 (Edge/Chromium). On Windows 11 the WebView2 "Evergreen"
    Runtime is preinstalled and auto-updated by the OS; on older Windows it may be absent and
    your app should detect it and offer the bootstrapper. The native loader shim
    (WebView2Loader.dll) must always ship next to your executable -- it is what P/Invoke binds
    to in order to find and start the runtime.

    References:
      - .NET application publishing / self-contained:
          https://learn.microsoft.com/dotnet/core/deploying/
      - Single-file deployment:
          https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
      - WebView2 distribution (Evergreen vs Fixed Version, loader, detection):
          https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
      - signtool (Authenticode):
          https://learn.microsoft.com/windows/win32/seccrypto/signtool

.PARAMETER AppName
    App / assembly name (the template produces PositronApp). Override to match your .csproj.

.PARAMETER Project
    The .csproj to publish. Defaults to "<AppName>.csproj" in the current directory.

.PARAMETER Version
    Version string embedded in the zip name.

.PARAMETER PublishDir
    Output directory for the publish payload.

.EXAMPLE
    ./windows-publish.ps1
    ./windows-publish.ps1 -AppName MyApp -Project ..\..\samples\Positron.Sample.Hello\Positron.Sample.Hello.csproj
#>
[CmdletBinding()]
param(
    [string]$AppName    = $(if ($env:APP_NAME)    { $env:APP_NAME }    else { 'PositronApp' }),
    [string]$Project    = $(if ($env:PROJECT)     { $env:PROJECT }     else { '' }),
    [string]$Version    = $(if ($env:VERSION)     { $env:VERSION }     else { '1.0.0' }),
    [string]$PublishDir = $(if ($env:PUBLISH_DIR) { $env:PUBLISH_DIR } else { './artifacts/win-x64' }),
    [string]$DistDir    = $(if ($env:DIST_DIR)    { $env:DIST_DIR }    else { './artifacts/dist' })
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = "$AppName.csproj"
}

# dotnet may not be on PATH in every shell; prefer PATH, fall back to a common install path.
$Dotnet = 'dotnet'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $candidate) { $Dotnet = $candidate }
    else { throw "dotnet not found. Install the .NET 11 SDK or add it to PATH." }
}

$Rid = 'win-x64'

Write-Host "==> Publishing $Project for $Rid (self-contained)"
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

# Multi-file self-contained publish (a folder of files). This is the recommended Positron
# default: it keeps WebView2Loader.dll visible as a sibling file and is the simplest to sign.
& $Dotnet publish $Project `
    -c Release `
    -r $Rid `
    --self-contained `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# --- Single-file variant (commented) -------------------------------------------------------
# A single-file publish bundles managed assemblies into one .exe. Native libraries such as
# WebView2Loader.dll are NOT loadable from inside the bundle, so you MUST let them extract to
# disk at startup -- IncludeNativeLibrariesForSelfExtract=true does that. Otherwise WebView2
# fails to initialise because the loader cannot be found.
#
# & $Dotnet publish $Project `
#     -c Release `
#     -r $Rid `
#     --self-contained `
#     -p:PublishSingleFile=true `
#     -p:IncludeNativeLibrariesForSelfExtract=true `
#     -o $PublishDir
# -------------------------------------------------------------------------------------------

# --- WebView2Loader.dll presence check -----------------------------------------------------
# The loader shim must ship next to the app. The WebView2 SDK/runtime package normally drops
# it into the publish output automatically; verify it is there rather than discovering a blank
# window at runtime.
$loader = Get-ChildItem -Path $PublishDir -Recurse -Filter 'WebView2Loader.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $loader) {
    Write-Warning "WebView2Loader.dll was NOT found in $PublishDir."
    Write-Warning "Positron needs the native loader next to the executable. Ensure the"
    Write-Warning "Microsoft.Web.WebView2 package is referenced (it supplies the loader), or"
    Write-Warning "copy WebView2Loader.dll from the SDK runtimes/win-x64/native folder."
} else {
    Write-Host "    WebView2Loader.dll present: $($loader.FullName)"
}

# --- Evergreen runtime detection note ------------------------------------------------------
# At RUNTIME your app should confirm the Evergreen Runtime is installed before creating the
# WebView, and if not, launch the bootstrapper. The canonical check is
# CoreWebView2Environment.GetAvailableBrowserVersionString(); a null/throw means "not
# installed" -> download + run MicrosoftEdgeWebview2Setup.exe (the ~2 MB Evergreen
# bootstrapper) from https://developer.microsoft.com/microsoft-edge/webview2/ . On Windows 11
# the runtime is already present, so this path is typically a no-op there.
#
# For offline or locked-down deployments, ship the "Fixed Version" runtime instead: a
# versioned folder you redistribute with the app and point WebView2 at explicitly. It is
# larger (~150 MB) but removes the install/update dependency. See the distribution doc above.

# --- Zip the folder for distribution -------------------------------------------------------
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }
$zipPath = Join-Path $DistDir "$AppName-$Version-$Rid.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Write-Host "==> Creating $zipPath"
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zipPath
Write-Host "==> Done: $zipPath"

# --- Code signing (commented) --------------------------------------------------------------
# Authenticode signing of the .exe. signtool now REQUIRES SHA-256 for both the file digest
# (/fd SHA256) and the timestamp digest (/td SHA256); SHA-1 is rejected. A timestamp server
# (/tr) is required so the signature stays valid after the certificate expires.
#
# NOTE: as of 2023+ the CA/Browser Forum requires OV and EV code-signing private keys to live
# on an HSM / hardware token (or a cloud signing service such as Azure Trusted Signing /
# SignTool's dlib provider). A plain .pfx file on disk no longer satisfies issuance for public
# trust, so /f mypfx.pfx works only for self-signed / internal certs.
#
# Hardware-token / cert-store cert (subject-name selected from the store):
#   & signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
#       /n "Your Company, Inc." "$PublishDir\$AppName.exe"
#
# Azure Trusted Signing (no local key material):
#   & signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 `
#       /dlib Azure.CodeSigning.Dlib.dll /dmdf metadata.json "$PublishDir\$AppName.exe"
#
# Re-zip AFTER signing so the distributed archive contains the signed binary.
# -------------------------------------------------------------------------------------------
