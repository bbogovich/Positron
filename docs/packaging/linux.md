# Packaging for Linux

Produces a Flatpak single-file bundle (`PositronApp.flatpak`).

Script: [`scripts/packaging/flatpak-build.sh`](https://github.com/bbogovich/Positron/tree/main/scripts/packaging/flatpak-build.sh)
Manifest set: `scripts/packaging/flatpak/` —
`io.github.positron.PositronApp.yaml` (manifest), `.desktop`, `.metainfo.xml`, `.svg` icon.

> **Note:** the Flatpak build steps are **untested on macOS** — `flatpak` and `flatpak-builder`
> are Linux-only. Run `flatpak-build.sh` on a Linux host or CI runner. The `dotnet publish` step
> works anywhere; only the `flatpak` commands require Linux.

## Why Flatpak, and why the GNOME runtime

Positron renders through **`libwebkit2gtk-4.1`** (WebKitGTK). The critical packaging fact:
that library ships in the **GNOME** Flatpak runtime (`org.gnome.Platform`), **not** the
freedesktop runtime. So the manifest bases on:

```yaml
runtime: org.gnome.Platform
runtime-version: '49'
sdk: org.gnome.Sdk
```

Version **49** is the GNOME runtime series that carries `libwebkit2gtk-4.1`. Because the
runtime provides WebKit, the app bundles **no** WebView engine of its own — consistent with
Positron's whole premise. If you based on `org.freedesktop.Platform` you would have to build and
bundle WebKitGTK yourself, which defeats the point.

## How the manifest is structured

`buildsystem: simple` — we do **not** compile inside flatpak-builder. Instead:

1. `flatpak-build.sh` runs `dotnet publish -r linux-x64 --self-contained` into
   `scripts/packaging/flatpak/publish/`.
2. The manifest consumes that directory as a local `dir` source and its `build-commands`:
   - copy the payload into `/app/positron/`,
   - install a tiny launcher `/app/bin/positron-app` that `exec`s the apphost,
   - install the `.desktop`, `.metainfo.xml`, and icon into the standard `/app/share` tree.

`finish-args` grant exactly what a WebKitGTK app needs and nothing more:

| Arg | Why |
|-----|-----|
| `--socket=wayland` | Native Wayland display. |
| `--socket=fallback-x11` | X11 fallback when Wayland is absent. |
| `--share=ipc` | WebKitGTK is multi-process; its processes need shared memory / IPC. |
| `--device=dri` | GPU access for accelerated compositing. |
| `--share=network` | Most web UIs load remote resources (drop it for a fully offline app). |
| `--filesystem=xdg-documents`, `--filesystem=xdg-download` | Minimal file access for open/save dialogs. |

## Prerequisites (Linux host)

```bash
flatpak install flathub org.flatpak.Builder
flatpak install flathub org.gnome.Platform//49 org.gnome.Sdk//49
```

.NET 11 SDK on `PATH` (or set `DOTNET`).

## Usage

```bash
cd scripts/packaging
./flatpak-build.sh
```

Override the app for your own project:

```bash
APP_ID=com.example.MyApp \
APP_NAME=MyApp \
PROJECT=../../src/MyApp/MyApp.csproj \
./flatpak-build.sh
```

The script:

1. publishes `linux-x64` self-contained into `flatpak/publish/`,
2. builds with `flatpak run org.flatpak.Builder --force-clean --repo=repo builddir <manifest>`,
3. exports a bundle with
   `flatpak build-bundle repo PositronApp.flatpak <app-id> --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo`.

`--runtime-repo` embeds where to fetch the GNOME runtime so a user installing the bundle pulls
the dependency automatically:

```bash
flatpak install --user scripts/packaging/flatpak/PositronApp.flatpak
flatpak run io.github.positron.PositronApp
```

## Format tradeoffs: Flatpak vs AppImage vs .deb

- **Flatpak** (this script): sandboxed, runtime-shared (WebKitGTK comes from `org.gnome.Platform`,
  patched by the runtime vendor), distributable via Flathub. Best default for a cross-distro
  desktop app, and the only format where you get WebKitGTK for free from the runtime.
- **AppImage**: a single portable file, no install, but **no shared runtime** — you must bundle
  WebKitGTK and its dependencies yourself, which is fragile across distros and inflates the file.
- **`.deb`/`.rpm`**: native package managers, but per-distro packaging work and you depend on the
  distro shipping a compatible `libwebkit2gtk-4.1` (most do, as `libwebkit2gtk-4.1-0`). Good when
  you target one distro family; poor for broad reach.

For broad, low-maintenance distribution with the WebKit dependency solved by the runtime,
Flatpak is the recommended path.

## References

- [Flatpak manifests](https://docs.flatpak.org/en/latest/manifests.html)
- [Available runtimes (GNOME)](https://docs.flatpak.org/en/latest/available-runtimes.html)
- [Single-file bundles](https://docs.flatpak.org/en/latest/single-file-bundles.html)
- [Flathub submission guide](https://docs.flathub.org/docs/for-app-authors/submission)
