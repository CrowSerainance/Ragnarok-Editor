# BrowEdit3 Alignment — ROMapOverlayEditor

This document describes how ROMapOverlayEditor reproduces the **asset and 3D environment** of [BrowEdit3](https://github.com/browedit-community/BrowEdit3) (reference: `BROWEDIT/BrowEdit 3/BrowEdit3-master`).

---

## 1. File formats (aligned with BrowEdit3)

| Asset | BrowEdit3 | ROMapOverlayEditor |
|-------|-----------|---------------------|
| **GND** | `Gnd.cpp`: GRGN, 2-byte version, 40+40 texture names, tiles (UVs, tex, lightmap, BGRA), cubes (h1–h4, tileUp/Front/Side) | `Map3D/GndReader.cs`: Same layout (version, textures 40+40, lightmaps skip, surfaces, cubes) |
| **RSW** | `Rsw.cpp`, `Rsw.Model.cpp`: objects, models (name, pos, rot, scale, file) | `Rsw/RswIO.cs`, `Rsw/RswFile.cs`: Same structure |
| **RSM** | `Rsm.cpp`: GRSM, meshes, hierarchy, offset matrix, vertices, faces | `Rsm/RsmParser.cs`, `Rsm/RsmFile.cs`: Same parsing |
| **GAT** | `Gat.cpp`: walkability grid | `Gat/GatIO.cs`, `Gat/GatMeshBuilder.cs`: Same |
| **Textures** | `gl/Texture.cpp`: stb_image (TGA, BMP, PNG, etc.), magenta transparency | `Imaging/TgaDecoder.cs` (TGA), `Map3D/TerrainBuilder.cs` + `RsmTextureResolver.cs`: TGA first, then WPF for BMP/PNG/JPG; magenta transparency in TgaDecoder |

---

## 2. Texture loading (BrowEdit3 → ROMapOverlayEditor)

- **BrowEdit3:** `Texture("data/texture/" + texture->file)` (Gnd.cpp line 69), stb_image loads from memory, magenta (255,0,255) → transparent.
- **ROMapOverlayEditor:**
  - Terrain: `Map3D/TerrainBuilder.cs` uses `data/texture/{name}`, then `{name}.tga`, `.bmp`, `.png`, `.jpg`. Decodes via `TgaDecoder.Decode()` (TGA) or WPF `BitmapImage` (others). Magenta transparency in `TgaDecoder.ApplyMagentaTransparency()`.
  - RSM models: `Rsm/RsmTextureResolver.cs` tries TGA first, then BMP/PNG/JPG.

---

## 3. 3D environment pipeline

| Component | BrowEdit3 | ROMapOverlayEditor |
|-----------|-----------|---------------------|
| **Terrain** | GndRenderer + GndShader, VAO/VBO, texture + lightmap | `TerrainBuilder.BuildTexturedTerrain()` → MeshGeometry3D per texture, DiffuseMaterial(ImageBrush), TGA support |
| **GAT overlay** | GatRenderer | `GatMeshBuilder.Build()` → colored quads (walkable / not) |
| **RSM models** | RsmRenderer + RsmShader, hierarchy, transforms | `RsmMeshBuilder.BuildFromRswModel()` → Model3DGroup, hierarchy (offset + position), instance transform (pos/rot/scale) |
| **Lights / Sounds / Effects** | Rsw.Light, Rsw.Sound, Rsw.Effect | `AddRswMarkers()` → small cubes at RSW positions |
| **Camera** | Orbit/pan/zoom | `BrowEditCameraController` (orbit, pan, zoom) |
| **Coordinates** | RO: X east, Y up, Z south; tileScale 10 | `BrowEditCoordinates.cs`: same conventions, RSW rotation → quaternion |

---

## 4. Reference assets (from BrowEdit3)

The `Data/` folder in this project is a **subset of BrowEdit3’s data** so the same assets can be used or referenced:

- **Data/texture/**  
  - `white.png` — default/white texture (BrowEdit3).  
  - `la_scifi/error_01.bmp` — error/missing texture (BrowEdit3).

- **Data/shaders_reference/**  
  - Copies of BrowEdit3’s GLSL shaders (`gnd.vs/fs`, `rsm.vs/fs`, `water.vs/fs`, etc.) for **reference only**. WPF cannot run GLSL; these document how BrowEdit3 renders terrain, models, and water.

These files are included in the build output (CopyToOutputDirectory). You can add the output `Data` folder (or your GRF/data path) as a VFS source so missing textures can fall back to `data/texture/white.png` or `data/texture/la_scifi/error_01.bmp` when available.

---

## 5. 3D Map Editor (RSW-centric, BrowEdit-style)

The **3D MAP EDITOR** tab is separated from the main Map Editor and focused on RSW/GND/GAT loading (like BrowEdit3’s “Open Map” → RSW list):

- **Own tab:** Main window has a **3D MAP EDITOR** tab with its own viewport and load controls.
- **Own data source (optional):** “Open GRF for 3D…” loads a GRF used only by the 3D view (no town list / map image). If you don’t use it, the 3D tab uses the same GRF as the Map Editor.
- **RSW-centric load:** “Load by name” (map name, e.g. `prontera`) or “Open 3D Map (RSW)…” resolves RSW → GND → GAT from the active VFS and builds terrain + GAT overlay + RSW markers. RSW models (trees, objects) are optional: **“Show RSW models”** is off by default so the broken/missing-texture blob is hidden until RSM rendering is fixed.
- **Fallback materials:** Missing RSM textures use **Magenta** so they are easy to spot.

The separate **3D GAT Editor** window (toolbar button) still exists and uses the same `GetVfsFor3D()` so it can use either the 3D-only GRF or the project GRF.

---

## 6. How to get the same look as BrowEdit3

1. **Use the same data source**  
   Open the same GRF (or folder) that BrowEdit3 uses (e.g. official RO `data.grf` or a `data/` folder with the same layout).

2. **Terrain textures**  
   Ensure “Terrain (GND textures)” is checked. Terrain uses `GndReader` + `TerrainBuilder` with TGA support so GND texture names resolve the same way as BrowEdit3 (`data/texture/` + file).

3. **RSM models**  
   RSW model objects are rendered via `RsmMeshBuilder` (full mesh, not placeholders). Models are loaded from `data/model/` + filename (e.g. `data/model/building.rsm`).

4. **Optional fallback**  
   Add the built `Data` folder (or a full RO `data` folder) as a VFS directory source so paths like `data/texture/white.png` resolve when not in GRF.

---

## 7. Differences (WPF vs OpenGL)

- **Lightmaps:** BrowEdit3 uses a second UV set and lightmap texture in the GND shader. WPF’s standard materials don’t support a second UV; lightmap data is parsed and skipped for now; terrain is diffuse-only.
- **Water:** BrowEdit3 has WaterRenderer + WaterShader. ROMapOverlayEditor has no water plane yet.
- **Effects/LUB:** BrowEdit3 has LubRenderer; ROMapOverlayEditor shows effect positions as simple markers only.
- **Custom shaders:** BrowEdit3 uses GLSL (Gnd, Rsm, Water, etc.); ROMapOverlayEditor uses WPF materials (DiffuseMaterial, ImageBrush). For pixel-perfect BrowEdit3 lighting/water you’d need a custom renderer (e.g. SharpDX) and port of the shaders.

---

## 8. Reference paths (BrowEdit3 repo)

- GND format: `browedit/components/Gnd.cpp`, `Gnd.h`
- Texture load: `browedit/gl/Texture.cpp` (stb_image, magenta)
- GND render: `browedit/components/GndRenderer.cpp`, `browedit/shaders/GndShader.h`
- RSM: `browedit/components/Rsm.cpp`, `RsmRenderer.cpp`
- RSW: `browedit/components/Rsw.cpp`, `Rsw.Model.cpp`
- Data assets: `data/texture/`, `data/shaders/`, `data/model/`
