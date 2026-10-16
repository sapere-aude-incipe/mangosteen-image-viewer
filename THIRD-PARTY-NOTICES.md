# Third-Party Notices

This file summarizes the main third-party components used by Mangosteen Image Viewer. The exact resolved dependency graph is pinned in `packages.lock.json` files and can be audited with:

```powershell
dotnet list .\src\Mangosteen\Mangosteen.csproj package --include-transitive
dotnet list .\tests\Mangosteen.Tests\Mangosteen.Tests.csproj package --include-transitive
```

## Icon assets

Most Mangosteen toolbar icons are original app-native vector geometry.

The 3D reset-view icon uses Microsoft Fluent UI System Icons (`Cube Sync 20 Regular`). Design previews also use `Arrow Counterclockwise 20 Filled` and `Arrow Clockwise 20 Filled`. The vector paths are from upstream commit `a563cf9166f4f91aa617557ed272612b7f0a2f72`. Fluent UI System Icons is licensed under the MIT License.

- Source: https://github.com/microsoft/fluentui-system-icons
- Copyright (c) 2020 Microsoft Corporation

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The rotate-left and rotate-right toolbar icons use Lucide's `rotate-ccw-square`
and `rotate-cw-square`. Design previews also use `rotate-ccw` and `rotate-cw`.
The path data is from upstream commit `500620a2e8123f8d1db191538886dc0c223f69a9`.
It is rendered as app-native geometry without a new runtime dependency.

- Source: https://github.com/lucide-icons/lucide

```text
ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

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

## Optional 3D component

The separately installable 3D component redistributes the official F3D 3.5.0
Windows x64 runtime. F3D is licensed under the BSD 3-Clause License and bundles
VTK plus optional model readers and their dependencies. The unmodified upstream
`share\licenses` directory is included in the component so every applicable
license and copyright notice travels with the binaries.

- F3D project: https://github.com/f3d-app/f3d
- F3D license: BSD-3-Clause
- Pinned archive: `F3D-3.5.0-Windows-x86_64.zip`
- SHA256: `db57f9fb7e1bbe2c022ec19dab3fd1eb38545f8c7b3d29d3906a951936a2e897`

## Notes for release maintainers

Before publishing a release, review the resolved lock files and published artifacts when dependencies are updated. Some native packages may include additional upstream notices in their package contents; keep this file updated when changing decoder, rendering, or installer dependencies.
