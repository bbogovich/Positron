#!/usr/bin/env bash
#
# macos-bundle.sh — build a macOS .app bundle for a Positron app.
#
# What it does:
#   1. `dotnet publish` the app TWICE — once for osx-arm64, once for osx-x64,
#      self-contained (the .NET runtime is bundled; no framework install needed).
#   2. `lipo`-merge the two apphost binaries into a single universal (fat) binary.
#   3. Hand-assemble PositronApp.app/Contents/{MacOS,Resources,Info.plist}, because .NET
#      has no first-party macOS .app bundler — you build the bundle layout yourself.
#   4. Copy the publish payload (managed/native deps + your wwwroot assets) into
#      Contents/MacOS alongside the universal apphost.
#   5. OPTIONALLY (only when SIGN_IDENTITY is set) codesign inside-out with the hardened
#      runtime, notarize with notarytool, and staple the ticket.
#
# Positron hosts the OS-native WKWebView via P/Invoke. WebKit.framework ships with macOS,
# so nothing WebView-related needs to be bundled — only the .NET app payload.
#
# Run with SIGN_IDENTITY unset to produce an UNSIGNED .app (fine for local runs; Gatekeeper
# will quarantine it on other machines until you sign + notarize).
#
# References:
#   - .NET RID-specific publish / self-contained:
#       https://learn.microsoft.com/dotnet/core/deploying/
#       https://learn.microsoft.com/dotnet/core/rid-catalog
#   - Apple: creating a universal binary with lipo:
#       https://developer.apple.com/documentation/apple-silicon/building-a-universal-macos-binary
#   - Apple: signing + notarizing a command-line / bundled app:
#       https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
#       https://developer.apple.com/documentation/security/customizing-the-notarization-workflow

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration — override any of these via the environment, e.g.
#   APP_NAME=MyApp BUNDLE_ID=com.example.myapp VERSION=1.2.3 ./macos-bundle.sh
# ---------------------------------------------------------------------------

# Name of the app and of the apphost binary dotnet produces. For the template, the .csproj
# is PositronApp.csproj, so the apphost is named "PositronApp".
APP_NAME="${APP_NAME:-PositronApp}"

# Reverse-DNS bundle identifier. Must be stable across releases.
BUNDLE_ID="${BUNDLE_ID:-io.github.positron.PositronApp}"

# Marketing + build version written into Info.plist.
VERSION="${VERSION:-1.0.0}"

# The .csproj to publish. Defaults to the template app name in the current directory; point
# it at the sample with e.g. PROJECT=../../samples/Positron.Sample.Hello/Positron.Sample.Hello.csproj
PROJECT="${PROJECT:-${APP_NAME}.csproj}"

# Where build artifacts land. PUBLISH_DIR holds the two per-RID publish outputs; DIST_DIR
# holds the finished .app.
PUBLISH_DIR="${PUBLISH_DIR:-./artifacts/publish}"
DIST_DIR="${DIST_DIR:-./artifacts/dist}"

# `dotnet` is not on PATH on every machine; allow an override. Falls back to PATH, then to
# the known Homebrew/installer location used on this project's dev machine.
if [[ -n "${DOTNET:-}" ]]; then
  :
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
elif [[ -x /usr/local/share/dotnet/dotnet ]]; then
  DOTNET="/usr/local/share/dotnet/dotnet"
else
  echo "error: dotnet not found. Set DOTNET=/path/to/dotnet." >&2
  exit 1
fi

# Location of this script, so the Info.plist / entitlements templates resolve regardless of
# the caller's working directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFO_PLIST_TEMPLATE="${SCRIPT_DIR}/Info.plist.template"
ENTITLEMENTS="${SCRIPT_DIR}/entitlements.plist"

# Publishing knobs. NativeAOT gives a single lean native binary and leaner entitlements
# (see entitlements.plist). Set POSITRON_AOT=0 to fall back to a plain self-contained
# (JIT CoreCLR) publish — in that case you must add allow-jit to entitlements.plist.
POSITRON_AOT="${POSITRON_AOT:-1}"

APP_BUNDLE="${DIST_DIR}/${APP_NAME}.app"
CONTENTS="${APP_BUNDLE}/Contents"

RIDS=(osx-arm64 osx-x64)

# ---------------------------------------------------------------------------
# 1. Publish per-RID, self-contained.
# ---------------------------------------------------------------------------
echo "==> Publishing ${PROJECT} for: ${RIDS[*]}"
rm -rf "${PUBLISH_DIR}" "${APP_BUNDLE}"
mkdir -p "${DIST_DIR}"

AOT_ARGS=()
if [[ "${POSITRON_AOT}" == "1" ]]; then
  AOT_ARGS+=("-p:PublishAot=true")
fi

for RID in "${RIDS[@]}"; do
  echo "    - ${RID}"
  # ${AOT_ARGS[@]+...} guards the expansion so `set -u` tolerates an empty array on the
  # ancient bash 3.2 that ships with macOS.
  "${DOTNET}" publish "${PROJECT}" \
    -c Release \
    -r "${RID}" \
    --self-contained \
    ${AOT_ARGS[@]+"${AOT_ARGS[@]}"} \
    -o "${PUBLISH_DIR}/${RID}"
done

# ---------------------------------------------------------------------------
# 2. lipo-merge the two apphosts into one universal binary.
#    Everything else in the payload (the .dylibs, the wwwroot assets) is taken from the
#    arm64 publish; native .dylibs that differ per-arch could also be lipo'd, but .NET's
#    self-contained native deps are RID-specific and we ship the arm64 set as the base.
#    For a truly universal payload you would lipo each matching .dylib pair too; most
#    Positron apps ship arch-appropriate native deps and only the apphost must be fat.
# ---------------------------------------------------------------------------
echo "==> Creating universal apphost with lipo"
UNIVERSAL_DIR="${PUBLISH_DIR}/universal"
# Start from the arm64 payload (managed assemblies are arch-neutral; wwwroot is data).
cp -R "${PUBLISH_DIR}/osx-arm64" "${UNIVERSAL_DIR}"

ARM64_HOST="${PUBLISH_DIR}/osx-arm64/${APP_NAME}"
X64_HOST="${PUBLISH_DIR}/osx-x64/${APP_NAME}"
if [[ ! -f "${ARM64_HOST}" || ! -f "${X64_HOST}" ]]; then
  echo "error: expected apphost '${APP_NAME}' in both publish outputs." >&2
  echo "       Check that AssemblyName matches APP_NAME (=${APP_NAME})." >&2
  exit 1
fi

lipo -create -output "${UNIVERSAL_DIR}/${APP_NAME}" "${ARM64_HOST}" "${X64_HOST}"
chmod +x "${UNIVERSAL_DIR}/${APP_NAME}"
echo "    merged -> $(lipo -archs "${UNIVERSAL_DIR}/${APP_NAME}")"

# ---------------------------------------------------------------------------
# 3. Assemble the .app layout.
# ---------------------------------------------------------------------------
echo "==> Assembling ${APP_BUNDLE}"
mkdir -p "${CONTENTS}/MacOS" "${CONTENTS}/Resources"

# Info.plist from template, substituting the three placeholders.
sed -e "s|__APP_NAME__|${APP_NAME}|g" \
    -e "s|__BUNDLE_ID__|${BUNDLE_ID}|g" \
    -e "s|__VERSION__|${VERSION}|g" \
    "${INFO_PLIST_TEMPLATE}" > "${CONTENTS}/Info.plist"

# ---------------------------------------------------------------------------
# 4. Copy the universal publish payload into Contents/MacOS.
# ---------------------------------------------------------------------------
echo "==> Copying payload into Contents/MacOS"
cp -R "${UNIVERSAL_DIR}/." "${CONTENTS}/MacOS/"

# Optional app icon: drop an AppIcon.icns next to this script and it will be bundled.
if [[ -f "${SCRIPT_DIR}/AppIcon.icns" ]]; then
  cp "${SCRIPT_DIR}/AppIcon.icns" "${CONTENTS}/Resources/AppIcon.icns"
fi

echo "==> Bundle assembled: ${APP_BUNDLE}"

# ---------------------------------------------------------------------------
# 5. Sign + notarize + staple — only when SIGN_IDENTITY is provided.
#    Signing is "inside-out": nested code (dylibs) first, the apphost, then the whole
#    bundle last. The hardened runtime (--options runtime) is required for notarization.
# ---------------------------------------------------------------------------
if [[ -z "${SIGN_IDENTITY:-}" ]]; then
  echo "==> SIGN_IDENTITY not set — skipping codesign / notarize / staple."
  echo "    The .app is UNSIGNED. It will launch locally but Gatekeeper will quarantine it"
  echo "    on other machines. To sign, set e.g.:"
  echo "        SIGN_IDENTITY=\"Developer ID Application: Your Name (TEAMID)\" \\"
  echo "        NOTARY_PROFILE=\"my-notary-profile\" $0"
  echo "==> Done: ${APP_BUNDLE}"
  exit 0
fi

echo "==> Signing with identity: ${SIGN_IDENTITY}"

# 5a. Sign every nested Mach-O (dylibs / helper binaries) first — inside-out.
#     -maxdepth avoids re-walking into already-signed frameworks needlessly.
find "${CONTENTS}/MacOS" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
  | while IFS= read -r -d '' NESTED; do
      echo "    sign dylib: ${NESTED#${CONTENTS}/}"
      codesign --force --timestamp --options runtime \
        --sign "${SIGN_IDENTITY}" "${NESTED}"
    done

# 5b. Sign the main apphost binary with entitlements + hardened runtime.
echo "    sign apphost: ${APP_NAME}"
codesign --force --timestamp --options runtime \
  --entitlements "${ENTITLEMENTS}" \
  --sign "${SIGN_IDENTITY}" "${CONTENTS}/MacOS/${APP_NAME}"

# 5c. Sign the whole bundle last.
echo "    sign bundle: ${APP_BUNDLE}"
codesign --force --timestamp --options runtime \
  --entitlements "${ENTITLEMENTS}" \
  --sign "${SIGN_IDENTITY}" "${APP_BUNDLE}"

# Verify the signature before shipping it to Apple.
codesign --verify --deep --strict --verbose=2 "${APP_BUNDLE}"

# 5d. Notarize. notarytool wants a single archive; zip the .app preserving symlinks.
#     Credentials come from a stored keychain profile (NOTARY_PROFILE) created once with:
#         xcrun notarytool store-credentials "my-notary-profile" \
#           --apple-id you@example.com --team-id TEAMID --password <app-specific-pw>
if [[ -z "${NOTARY_PROFILE:-}" ]]; then
  echo "==> SIGN_IDENTITY set but NOTARY_PROFILE not set — signed but NOT notarized."
  echo "    Set NOTARY_PROFILE to a stored notarytool credential profile to notarize + staple."
  echo "==> Done (signed, un-notarized): ${APP_BUNDLE}"
  exit 0
fi

NOTARIZE_ZIP="${DIST_DIR}/${APP_NAME}-notarize.zip"
echo "==> Notarizing via notarytool (profile: ${NOTARY_PROFILE})"
/usr/bin/ditto -c -k --keepParent "${APP_BUNDLE}" "${NOTARIZE_ZIP}"
xcrun notarytool submit "${NOTARIZE_ZIP}" --keychain-profile "${NOTARY_PROFILE}" --wait

# 5e. Staple the notarization ticket onto the .app so it validates offline.
echo "==> Stapling ticket"
xcrun stapler staple "${APP_BUNDLE}"
xcrun stapler validate "${APP_BUNDLE}"
rm -f "${NOTARIZE_ZIP}"

echo "==> Done (signed + notarized + stapled): ${APP_BUNDLE}"
