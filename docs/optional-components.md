# Optional Rendering Components

Mangosteen keeps its normal WPF and SkiaSharp image path as the default. Optional
components are discovered only when their `component.json` manifest is present
under the application's `components` directory. They are not loaded at startup
and do not add work to ordinary image viewing.

## GPU acceleration for large images

The large-image component is intended for exceptionally large still images. It
is selected when the estimated decoded RGBA image is at least 256 MiB, or when a
PSB file is opened.

- libvips decodes only the visible 512-pixel tiles and a one-tile margin.
- A power-of-two pyramid supplies a suitable resolution for the current zoom.
- OpenGL textures are kept within a 384 MiB in-memory budget.
- Losslessly compressed tiles are cached under
  `%LocalAppData%\Mangosteen Image Viewer\Cache\LargeImageTiles` and invalidated
  when the source file changes.
- 8-bit and 16-bit channels are preserved in the tile path. Embedded ICC profiles
  are converted to sRGB for display when libvips can read them.
- The existing preview remains visible while sharper tiles arrive.
- Tile requests are bounded and obsolete requests are cancelled after panning,
  zooming, or changing files. A disk-cache write failure does not prevent display;
  a decoding or texture-upload failure falls back to the standard image path.

Some compressed formats cannot provide truly independent regions. In those
cases libvips may still need to perform more decoding work than a tiled TIFF or
pyramidal source, but Mangosteen avoids retaining a full uncompressed image.

## 3D model viewing

The 3D component embeds the F3D 3.5.0 engine in Mangosteen's native OpenGL
surface. Initial browsing support covers STL, PLY, OBJ, glTF, and GLB files.

- Left-drag orbits the camera.
- The mouse wheel or the bottom toolbar's zoom control zooms. Its percentage is
  relative to the initial fitted view, not a pixel scale.
- Reset view (or `F`) restores the starting angle and fits the model again.
- Image rotation controls remain disabled while viewing a model.
- Models are framed automatically against a fading floor grid.
- A lower-right XYZ orientation indicator remains visible.
- Materials, textures, vertex colors, and animations are handled by F3D when
  present in a supported file.
- A model import does not block navigation or window controls. F3D's native
  import itself cannot be interrupted mid-call; cancelled results are discarded
  and a newer import starts when the engine is available.

The official F3D Windows runtime is comparatively large, so it remains a separate
component. Its complete license bundle is included with the component.

## Getting the advanced components

Download `Mangosteen-Complete-Portable-<version>-x64.zip` to get both components
already installed. The standard installer and standard portable ZIP contain only
the core image viewer, and separate component archives are not published. The
unpacked complete build is available at `publish\complete-portable\Mangosteen.exe`
for local testing.

For source-tree development, the following environment variables enable routing
without copying the lightweight manifest:

```powershell
$env:MANGOSTEEN_ENABLE_GPU_LARGE_IMAGES = "1"
$env:MANGOSTEEN_ENABLE_3D_VIEWER = "1"
```

The 3D runtime must still be staged under `components\model-viewer` for F3D to
load. Release packaging downloads a pinned archive and verifies its SHA256 hash.

## Native rendering regression test

The 3D pixel test uses hidden rendering surfaces and needs the optional runtime
and a working Windows OpenGL driver. It checks loading from the 1x1 parked host,
resizing, actual visible geometry, orbit, zoom, reset, theme changes, and reopening.
It is skipped when the runtime is not explicitly configured:

```powershell
$env:MANGOSTEEN_TEST_F3D_DIRECTORY = (Resolve-Path publish/complete-portable/components/model-viewer).Path
dotnet test tests/Mangosteen.Tests/Mangosteen.Tests.csproj -c Release --filter FullyQualifiedName~F3dViewportTests
```
