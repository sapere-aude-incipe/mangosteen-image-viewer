# Optional Rendering Components

Mangosteen keeps its normal WPF and SkiaSharp image path as the default. GPU
large-image rendering is built into every edition and controlled by a setting.
3D model viewing is an optional component, discovered only when its
`component.json` manifest is present under the application's `components`
directory. Neither is loaded at startup or adds work to ordinary image viewing.

## GPU acceleration for large images

The large-image renderer is intended for exceptionally large still images. It
is selected when the estimated decoded RGBA image is at least 256 MiB, or when a
PSB file is opened, and **Options > Performance > GPU acceleration for large
images** is on.

The setting is automatic until the user changes it: Mangosteen asks DXGI for the
graphics adapters the first time a large image needs the answer (never at
startup) and turns it on when a hardware adapter outside Microsoft's own
(Basic Render Driver, Hyper-V) reports at least 512 MB of dedicated video
memory. Integrated GPUs typically report 128 MB or less. Once chosen, the
explicit on/off value is stored as `IsGpuLargeImageRenderingEnabled` in
`settings.json`. On Windows' OpenGL 1.1 software renderer ("GDI Generic") the
renderer stands aside regardless of the setting.

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
surface. Browsing covers meshes and scenes (STL, PLY, OBJ, glTF/GLB, FBX, COLLADA, 3DS, 3MF, Alembic, USD), CAD and BIM (STEP, IGES, BREP, IFC), VTK and simulation data, volumes (MetaImage, NRRD, DICOM, VDB) and point clouds or Gaussian splats (LAS/LAZ, PCD, SPLAT); the full list is in
`src/Mangosteen/Rendering/Advanced/ModelFileExtensions.cs`, together with the per-format F3D
options (volume rendering, point size, splat sprites) that the F3D app would apply from its
config files.

- Left-drag orbits the camera.
- The mouse wheel or the bottom toolbar's zoom control zooms. Its percentage is
  relative to the initial fitted view, not a pixel scale.
- Reset view (or `F`) restores the starting angle and fits the model again.
- Image rotation controls remain disabled while viewing a model.
- Models are framed automatically against a fading floor grid.
- The grid sits beneath the model by default, without moving its geometry.
  **Options > 3D view > Grid at world origin** anchors the grid to the original
  coordinate origin instead. This preference is saved and changes only the grid,
  not the camera or model file.
- A lower-right XYZ orientation indicator remains visible.
- Materials, textures, vertex colors, and animations are handled by F3D when
  present in a supported file.
- A model import does not block navigation or window controls. F3D's native
  import itself cannot be interrupted mid-call; cancelled results are discarded
  and a newer import starts when the engine is available.

The official F3D Windows runtime is comparatively large, so it remains a separate
component. Its complete license bundle is included with the component.

### Model read-ahead

The **Preload nearby files** setting also reads up to two nearby local model files
ahead after the configured idle delay. It uses a small pooled buffer and Windows'
file cache, not an additional retained copy of the model or a native graphics
context. Navigation, disabling preloading, and shutdown cancel the worker.
Changed files are eligible for read-ahead again.

Read traffic is capped at the smaller of one sixteenth of the selected preload
budget or 128 MiB per pass, and 64 MiB per model. Larger models receive partial
read-ahead. Network locations and external material, texture, and buffer files
are excluded. Windows may evict prefetched pages as needed.

This is not a decoded-scene/GPU cache: F3D still parses and uploads a model when
it is selected. It reduces potential disk latency without competing for the
active viewport's graphics context. Actual improvement depends on the storage
device, Windows' existing cache, and the model's import cost.

## Getting 3D model viewing

Run `Mangosteen-Setup-<version>-x64.exe` and choose the **Full** setup type, or
download `Mangosteen-Complete-Portable-<version>-x64.zip` to get the 3D viewer
already included. The installer defaults to the Core type, which installs only
the core image viewer; the 3D file types are registered only with Full. The
standard portable ZIP has no 3D viewer, and separate component archives are not
published. Upgrading removes the retired `components\gpu-large-images` switch
folder from earlier installs. The
unpacked complete build is available at `publish\complete-portable\Mangosteen.exe`
for local testing.

For source-tree development, this environment variable enables 3D routing
without copying the lightweight manifest:

```powershell
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
