# Third-Party Notices

This file summarizes the main third-party components used by Mangosteen Image Viewer. The exact resolved dependency graph is pinned in `packages.lock.json` files and can be audited with:

```powershell
dotnet list .\src\Mangosteen\Mangosteen.csproj package --include-transitive
dotnet list .\tests\Mangosteen.Tests\Mangosteen.Tests.csproj package --include-transitive
```

## Icon assets

Mangosteen toolbar icons are original app-native vector geometry. They are not copied from a third-party icon pack.

## Runtime NuGet dependencies

| Component | Version | License | Purpose |
| --- | --- | --- | --- |
| Magick.NET-Q16-HDRI-OpenMP-x64 | 14.13.1 | Apache-2.0 | Broad image decoding fallback through ImageMagick. |
| Magick.NET.Core | 14.13.1 | Apache-2.0 | Core Magick.NET APIs. |
| NetVips | 3.2.0 | MIT | Managed .NET binding for libvips. |
| NetVips.Native.win-x64 | 8.18.2 | MIT package license; bundles libvips native binaries and related native dependencies | Windows x64 native libvips runtime. |
| SkiaSharp | 3.119.4 | MIT | Skia-backed rendering and fast raster decoding. |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT package license; bundles Skia native binaries | Windows native Skia runtime. |
| SkiaSharp.Views.WPF | 3.119.4 | MIT | WPF render surface integration. |
| OpenTK and OpenTK.GLWpfControl packages | 4.3.0 / 4.2.3 | MIT | Transitive dependency of SkiaSharp WPF views. |

## Test-only NuGet dependencies

| Component | Version | License | Purpose |
| --- | --- | --- | --- |
| MSTest | 4.0.2 | MIT | Unit test framework, adapter, and test SDK dependency bundle. |

## Operating system components

Mangosteen can use Windows Imaging Component (WIC), which is provided by Windows. It is not redistributed by this project.

## Notes for release maintainers

Before publishing a release, review the resolved lock files and published artifacts when dependencies are updated. Some native packages may include additional upstream notices in their package contents; keep this file updated when changing decoder, rendering, or installer dependencies.
