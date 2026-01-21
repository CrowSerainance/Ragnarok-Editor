# Ragnarok Editor (RO Map Overlay Editor)

WPF map overlay editor for Ragnarok Online: edit NPCs, warps, and spawns on client maps. Loads maps and Towninfo from GRF or from an optional Lua data folder.

## Requirements

- .NET 8 (Windows)
- Visual Studio 2022 or later, or `dotnet` CLI

## Build & run

```bash
dotnet build ROMapOverlayEditor\ROMapOverlayEditor.csproj
dotnet run --project ROMapOverlayEditor\ROMapOverlayEditor.csproj
```

Or open `ROMapOverlayEditor.sln` in Visual Studio.

## Usage

1. **Open GRF** — Pick a `.grf` (e.g. `data.grf`) for map images and assets.
2. **Set Lua Folder** (optional) — If Towninfo isn’t in the GRF or is bytecode, point to a folder with `Towninfo.lua` or `Towninfo.lub`.
3. **Town** — Select a town to load the map and NPCs.
4. **Copy Export** — Copies markdown (rAthena script, Lua, changelog) to the clipboard.

## License

See repository for license information.
