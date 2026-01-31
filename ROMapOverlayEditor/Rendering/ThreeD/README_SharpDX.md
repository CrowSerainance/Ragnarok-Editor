# SharpDX 3D View — Excluded from Build

`ThreeDMapView`, `GatOverlayMeshBuilder`, and related SharpDX types are **excluded** because the WPF `wpftmp` (markup compile) project does not resolve `HelixToolkit.Wpf.SharpDX` / `HelixToolkit.Maths` (e.g. `MeshGeometry3D`, `PhongMaterialCore`, `Vector3`, `DefaultEffectsManager`).

## In place (Media3D)

- **BrowEditViewConfig** – FOV, camera speed, background, GAT overlay toggle.
- **GndParser**, **GndTerrainMeshBuilder**, **ParsedGnd** / **ParsedGndTile** – GND 1.7+ and terrain mesh.
- **GatMeshBuilder** – GAT overlay in the current Helix Media3D view.
- **View Options** in `GatEditorView`: GAT Overlay, Camera Mouse Speed, Reset View.

## Re‑enabling SharpDX 3D

1. **Class library** – Create e.g. `ROMapOverlayEditor.ThreeD.SharpDX` referencing `HelixToolkit.Wpf.SharpDX` / `HelixToolkit.SharpDX`. Move `ThreeDMapView`, `GatOverlayMeshBuilder`, and any `RswObjectPrimitiveRenderer` / `TerrainMaterialBuilder` there. Reference from the main WPF app so `wpftmp` only compiles app XAML.

2. **Fix wpftmp refs** – If the markup compile can inherit Helix SharpDX, remove the `<Compile Remove="..."/>` and `<Page Remove="..."/>` for `ThreeDMapView` and `GatOverlayMeshBuilder` in the `.csproj`.

3. **Hook-up** – In `GatEditorView`, host `ThreeDMapView` instead of `HelixViewport3D`; on load: parse GND, build terrain and GAT overlay, call `SetTerrainMesh` / `SetGatOverlay`; wire `BrowEditViewConfig` to View Options.
