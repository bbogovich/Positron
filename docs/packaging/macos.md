# Packaging for macOS

Produces a universal (`arm64` + `x86_64`) `.app` bundle, optionally code-signed, notarized,
and stapled for distribution outside the Mac App Store.

Script: [`scripts/packaging/macos-bundle.sh`](https://github.com/bbogovich/Positron/tree/main/scripts/packaging/macos-bundle.sh)
Supporting files: `Info.plist.template`, `entitlements.plist`.

## Why it is hand-assembled

.NET has **no first-party macOS `.app` bundler**. `dotnet publish` gives you a bare apphost
and its payload; turning that into a Finder-launchable, signable, notarizable bundle is
manual work. The script automates the five steps Apple's tooling expects:

1. **Publish per-RID** — `dotnet publish -c Release -r osx-arm64 --self-contained` and again
   for `osx-x64`. Self-contained means the .NET runtime is bundled; the end user needs no
   .NET install. WKWebView comes from the OS, so nothing WebView-related is bundled.
2. **`lipo`-merge** the two apphosts into one universal binary
   ([Apple: building a universal binary](https://developer.apple.com/documentation/apple-silicon/building-a-universal-macos-binary)).
3. **Assemble** `PositronApp.app/Contents/{MacOS,Resources,Info.plist}` by hand.
4. **Copy** the publish payload (managed assemblies, native deps, your `wwwroot`) into
   `Contents/MacOS` next to the universal apphost.
5. **Sign → notarize → staple** (only when `SIGN_IDENTITY` is set).

## Prerequisites

- macOS with Xcode command-line tools (`lipo`, `codesign`, `xcrun`, `stapler`).
- .NET 11 SDK. If `dotnet` is not on `PATH`, set `DOTNET=/path/to/dotnet`.
- For signing/notarizing only: an Apple **Developer ID Application** certificate in the
  keychain and a stored `notarytool` credential profile.

## Usage

Unsigned bundle (fine for local use; Gatekeeper will quarantine it on other machines):

```bash
cd scripts/packaging
./macos-bundle.sh
```

Package your own app by overriding the variables at the top of the script:

```bash
APP_NAME=MyApp \
BUNDLE_ID=com.example.myapp \
VERSION=1.2.3 \
PROJECT=../../src/MyApp/MyApp.csproj \
./macos-bundle.sh
```

The finished bundle lands in `artifacts/dist/<APP_NAME>.app`. Confirm it:

```bash
plutil -lint artifacts/dist/MyApp.app/Contents/Info.plist
lipo -archs  artifacts/dist/MyApp.app/Contents/MacOS/MyApp   # -> x86_64 arm64
open artifacts/dist/MyApp.app
```

### Signing, notarizing, stapling

Signing is **inside-out**: nested Mach-O files (`.dylib`/`.so`) first, then the apphost (with
entitlements), then the whole bundle last. The hardened runtime (`--options runtime`) is
required for notarization. The script does all of this when you provide credentials:

```bash
# One-time: store notary credentials in the keychain.
xcrun notarytool store-credentials "positron-notary" \
  --apple-id you@example.com --team-id TEAMID --password <app-specific-password>

SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
NOTARY_PROFILE="positron-notary" \
./macos-bundle.sh
```

- `SIGN_IDENTITY` set, `NOTARY_PROFILE` unset → signed but not notarized.
- Both unset → unsigned (the default; an echoed note explains the skip).

See Apple's
[Notarizing macOS software before distribution](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
and [Customizing the notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

## Entitlements and NativeAOT

`entitlements.plist` is deliberately **minimal**. Positron publishes with NativeAOT by
default (`POSITRON_AOT=1`): no JIT, no runtime code generation. That lets the file omit the
JIT-oriented entitlements that a CLR normally needs:

- `com.apple.security.cs.allow-jit` — **omitted** (NativeAOT never JITs).
- `com.apple.security.cs.allow-unsigned-executable-memory` — **omitted** (JIT-only).
- `com.apple.security.cs.allow-dyld-environment-variables` — **omitted** (not needed).
- `com.apple.security.cs.disable-library-validation` — **omitted**; Positron only `dlopen`s
  Apple-signed system frameworks, so library validation stays on.

If you set `POSITRON_AOT=0` to publish a plain self-contained (JIT CoreCLR) build, you **must**
add `com.apple.security.cs.allow-jit` to `entitlements.plist` or the app will fail the hardened
runtime at launch. The comments in `entitlements.plist` spell out exactly which entitlement to
add for which symptom.

## Info.plist

`Info.plist.template` carries placeholders (`__APP_NAME__`, `__BUNDLE_ID__`, `__VERSION__`) that
the script substitutes with `sed`. It sets `CFBundlePackageType=APPL`, `NSHighResolutionCapable`
(Retina), and `LSMinimumSystemVersion=11.0` (the Big Sur baseline matching the publish RIDs).

## Distribution format tradeoffs

For most open-source Positron apps a signed + notarized **`.app` inside a `.dmg` or `.zip`** is
the right default — it is the lowest-friction path and requires no first-party installer
tooling. A `.pkg` installer is worth it only when you need pre/post-install scripts or to place
files outside the app bundle. The Mac App Store imposes additional sandboxing and review and is
out of scope for this script.
