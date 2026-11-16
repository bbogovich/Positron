#!/usr/bin/env bash
#
# flatpak-build.sh — build a Positron app flatpak and export a single-file .flatpak bundle.
#
# Steps:
#   1. `dotnet publish` the app for linux-x64, self-contained, into the directory the manifest
#      consumes as its `dir` source (scripts/packaging/flatpak/publish).
#   2. Build the flatpak with org.flatpak.Builder into a local OSTree repo.
#   3. Export a distributable single-file bundle (PositronApp.flatpak).
#
# The app renders through libwebkit2gtk-4.1, which the GNOME runtime (org.gnome.Platform//49)
# provides — so nothing WebKit-related is bundled; see the manifest for the rationale.
#
# !!! UNTESTED ON macOS !!!
# flatpak / flatpak-builder are Linux-only tools. This script is meant to run on a Linux host
# (or a Linux CI runner / container). It will not run on the macOS dev machine; it is provided
# so the Linux packaging path is complete and reproducible. The dotnet publish step works
# anywhere, but the flatpak steps require a Linux flatpak installation.
#
# Prerequisites on the Linux host:
#   flatpak install flathub org.flatpak.Builder
#   flatpak install flathub org.gnome.Platform//49 org.gnome.Sdk//49
#
# References:
#   - flatpak-builder:        https://docs.flatpak.org/en/latest/flatpak-builder.html
#   - Single-file bundles:    https://docs.flatpak.org/en/latest/single-file-bundles.html
#   - Flathub repo for deps:  https://flathub.org/

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration — override via environment.
# ---------------------------------------------------------------------------
APP_ID="${APP_ID:-io.github.positron.PositronApp}"
APP_NAME="${APP_NAME:-PositronApp}"

# The .csproj to publish. Defaults to the template app in the current directory; point it at
# the sample with PROJECT=../../samples/Positron.Sample.Hello/Positron.Sample.Hello.csproj
PROJECT="${PROJECT:-${APP_NAME}.csproj}"

DOTNET="${DOTNET:-dotnet}"
RID="linux-x64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FLATPAK_DIR="${SCRIPT_DIR}/flatpak"
MANIFEST="${FLATPAK_DIR}/${APP_ID}.yaml"

# The manifest's `dir` source reads from <flatpak>/publish.
PUBLISH_DIR="${PUBLISH_DIR:-${FLATPAK_DIR}/publish}"

# flatpak-builder working dirs (local to the flatpak manifest dir).
BUILD_DIR="${FLATPAK_DIR}/builddir"
REPO_DIR="${FLATPAK_DIR}/repo"
BUNDLE_OUT="${BUNDLE_OUT:-${FLATPAK_DIR}/${APP_NAME}.flatpak}"

# ---------------------------------------------------------------------------
# 1. Publish linux-x64, self-contained, into the manifest's publish/ source dir.
# ---------------------------------------------------------------------------
echo "==> Publishing ${PROJECT} for ${RID} (self-contained)"
rm -rf "${PUBLISH_DIR}"
"${DOTNET}" publish "${PROJECT}" \
  -c Release \
  -r "${RID}" \
  --self-contained \
  -o "${PUBLISH_DIR}"

# Sanity-check: the apphost the launcher execs must exist and be named APP_NAME.
if [[ ! -f "${PUBLISH_DIR}/${APP_NAME}" ]]; then
  echo "error: expected apphost '${APP_NAME}' in ${PUBLISH_DIR}." >&2
  echo "       Ensure AssemblyName matches APP_NAME (=${APP_NAME}); the launcher execs" >&2
  echo "       /app/positron/${APP_NAME}." >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# 2. Build the flatpak.
# ---------------------------------------------------------------------------
echo "==> Building flatpak with org.flatpak.Builder"
rm -rf "${BUILD_DIR}" "${REPO_DIR}"
flatpak run org.flatpak.Builder --force-clean --repo="${REPO_DIR}" "${BUILD_DIR}" "${MANIFEST}"

# ---------------------------------------------------------------------------
# 3. Export a single-file bundle. --runtime-repo embeds where to fetch the GNOME runtime so a
#    user installing the bundle can pull the dependency automatically.
# ---------------------------------------------------------------------------
echo "==> Exporting bundle: ${BUNDLE_OUT}"
flatpak build-bundle "${REPO_DIR}" "${BUNDLE_OUT}" "${APP_ID}" \
  --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo

echo "==> Done: ${BUNDLE_OUT}"
echo "    Install locally with:  flatpak install --user ${BUNDLE_OUT}"
echo "    Run with:              flatpak run ${APP_ID}"
