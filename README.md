# Mangosteen Image Viewer

![Mangosteen Image Viewer banner](docs/assets/mangosteen-banner.png)

[![CI](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/actions/workflows/ci.yml/badge.svg)](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/sapere-aude-incipe/mangosteen-image-viewer?include_prereleases&label=release)](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

### [Download Portable (.zip)](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases) | [Download Installer (.exe)](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases)

Mangosteen is a simple and fast Windows image viewer inspired by the classic Windows Photo Viewer experience. It focuses on quick image navigation, smooth zooming, actual-pixel viewing, broad format support, animated GIF playback, and responsive handling of very large images.

## Screenshots

| Dark theme | Light theme |
| --- | --- |
| ![Mangosteen Image Viewer showing an image in dark theme](docs/screenshots/mangosteen-dark.png) | ![Mangosteen Image Viewer showing an image in light theme](docs/screenshots/mangosteen-light.png) |

## Features

- Fast previous/next folder navigation.
- Mouse-wheel zoom.
- Left-button drag panning for oversized images.
- Actual-pixel viewing for `1:1` physical pixel mapping.
- Smooth or nearest-neighbor upscaling.
- Light and dark themes.
- Smart preloading with a configurable memory budget.
- RAW support with initial preview loading.
- Animated GIF support.
- Installer and portable zip builds for Windows x64.

## Repository Layout

```text
src/Mangosteen/          WPF app, rendering, decoding, navigation, and caching code
tests/Mangosteen.Tests/  Unit tests
packaging/inno/          Inno Setup installer script
scripts/                 Local packaging helpers
docs/                    Screenshots, logos, and longer-form project documentation
```

## Format Support

Mangosteen uses a decoder chain instead of relying on one hard-coded codec path:

1. WIC embedded RAW previews
2. libvips
3. Windows Imaging Component
4. SkiaSharp
5. Magick.NET fallback

The goal is broad practical coverage for common formats such as JPEG, PNG, BMP, GIF, TIFF, WebP, AVIF, HEIC/HEIF, and several RAW-family formats. Exact support depends on file contents and, for some Windows-native paths, installed codecs.

## Download

Grab the newest build from the [Releases](https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases) page:

- **Portable**: `Mangosteen-Portable-<version>-x64.zip` — extract anywhere and run `Mangosteen.exe`. No installation and no separate .NET runtime required.
- **Installer**: `Mangosteen-Setup-<version>-x64.exe` — classic setup with Start menu shortcut.

The first public releases are unsigned while the project builds enough public reputation for open-source code signing.

To verify a download, also grab `SHA256SUMS.txt` and compare hashes before running:

```powershell
Get-FileHash .\Mangosteen-Setup-0.1.0-x64.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Windows SmartScreen may warn for unsigned preview releases.

## Build

Requirements:

- Windows 10 or later.
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Inno Setup 6, only if you want to build the installer.

Build and test:

```powershell
dotnet restore Mangosteen.slnx --locked-mode
dotnet build Mangosteen.slnx --configuration Release --no-restore
dotnet test Mangosteen.slnx --configuration Release --no-build
```

Run from source:

```powershell
dotnet run --project .\src\Mangosteen\Mangosteen.csproj -- "C:\path\to\image.jpg"
```

Build release artifacts:

```powershell
dotnet restore .\src\Mangosteen\Mangosteen.csproj --runtime win-x64 --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

This creates:

- `dist\Mangosteen-Setup-<version>-x64.exe`
- `dist\Mangosteen-Portable-<version>-x64.zip`
- `dist\SHA256SUMS.txt`

## Controls

- `Left` / `Right`: previous / next image.
- Mouse wheel: zoom around the cursor.
- Left mouse drag: pan.
- `F`: fit to window.
- `Ctrl+O`: open an image.

## Options

- Upscaling: `Smooth` uses interpolation when zooming in; `Nearest` keeps hard pixel edges for pixel art and similar images.
- Preload nearby images: decodes likely next and previous images in the background so folder navigation can feel instant.
- Preload memory: limits how much RAM Mangosteen may use for decoded-image cache entries.
- Preload aggressiveness: controls how far ahead Mangosteen scans and how eagerly it warms likely next images.

## Release Process

CI builds and tests every push and pull request on Windows using NuGet lock files. Tagged releases named like `v0.1.0` run the release workflow, validate the version, build unsigned Windows artifacts, verify `SHA256SUMS.txt`, upload artifacts, and create a prerelease.

The intended signing path is SignPath Foundation once the project has enough public reputation for open-source code signing.

## License

Mangosteen Image Viewer is released under the MIT License. See [LICENSE](LICENSE).
