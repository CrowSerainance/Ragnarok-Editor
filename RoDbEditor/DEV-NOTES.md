## RoDbEditor – Developer Notes (Data & Client Modes)

### Server DB pipeline (rAthena)

- **DataPath** points to the rAthena root directory.
- Base DB files (`db/re`, `db/pre-re`, `db`) are treated as **read-only**.
- All edits and custom entries are written into `db/import`:
  - Items: `db/import/item_db.yml` via `ItemDbService.SaveItem`.
  - Mobs: `db/import/mob_db.yml` via `MobDbService.SaveMob`.
  - Mob skills: `MobSkillDbService` is read-only and exposes source file/line for manual editing.
- `ItemDbService` also owns the conventions for:
  - Custom item IDs (≥ 50000) → always routed to `db/import/item_db.yml`.
  - Custom headgear `View` IDs (helper `GetNextCustomViewId(start)`; default 32000+).

### Client configuration and write modes

- `RoDbEditorConfig` is the single source of truth for:
  - `DataPath` – rAthena root (writes only to `db/import`).
  - `ClientRootPath` – base client install (contains `System/` and GRFs).
  - `ClientPatchRoot` – patch output root (overlay `System/` + `data/` files).
  - `TargetGrfPath` / `TargetGrfFileName` – writable custom GRF for sprites and accessory tables.
  - `GrfPaths` – ordered GRF list for reads (mirrors `DATA.ini` priority where possible).
  - `ClientWriteMode` – how RoDbEditor is allowed to write client-side data:
    - `PatchOnly` (default, safer).
    - `LiveClient` (direct writes into the client install).

- `WorkspaceProfile` mirrors these fields per profile and is applied via `App.ApplyWorkspaceProfile`,
  which:
  - Syncs the active profile into legacy config fields.
  - Rebuilds GRF services, sprite sources, and client/server data services.
  - Honors `ClientWriteMode` when wiring writers.

### Client itemInfo strategy

There are two itemInfo writers, orchestrated by `ClientWriteMode`:

- `ItemInfoLuaWriter` (direct `System/itemInfo_C.lua` writer):
  - Used only in **LiveClient** mode.
  - Writes/updates `tbl_custom` entries in `itemInfo_C.lua` under `ClientRootPath/System(EN)` or `System`.
  - Intended for single-user or local test clients where editing the install is acceptable.

- `ClientItemInfoWriter` (overlay writer):
  - Always available; generates `System/itemInfo_rodbeditor.lua` into `ClientPatchRoot`.
  - Calls `dofile("System/itemInfo.lub")` then merges custom entries (via `AddItem` or table merge).
  - Intended for distribution via patchers/GRF overlays and for multi-client setups.

**Invariant:** Only one path should be considered authoritative per workflow:

- In **PatchOnly** mode:
  - Treat `ClientRootPath` and its `System` scripts as **read-only**.
  - Prefer `ClientItemInfoWriter` and patch overlay files under `ClientPatchRoot`.
  - `App` will not construct `ItemInfoLuaWriter` in this mode.

- In **LiveClient** mode:
  - Allow `ItemInfoLuaWriter` to update `System/itemInfo_C.lua` under `ClientRootPath`.
  - `ClientItemInfoWriter` can still be used explicitly for patch-style workflows, but UI/flows
    should avoid creating conflicting duplicate definitions.

### Headgear View IDs and accessory tables

- `MainWindow.ValidateItemEntry` + `ItemDbService.GetNextCustomViewId` enforce:
  - Headgear / costume headgear with `Head_` / `Costume_Head_` locations must have a positive `View`.
  - Missing `View` values are auto-assigned from the next free View ID (default base 32000).
  - Duplicate `View` IDs across items produce diagnostics/warnings.

- `AccessoryIdWriter` is the single writer for headgear mapping tables:
  - Merges base tables from `data.grf` with any existing custom tables.
  - Writes `accessoryid.lua` / `.lub` and `accname.lua` / `.lub` into a **writable custom GRF**:
    - Primary paths: `data/luafiles514/lua files/datainfo/accessoryid.lua` / `accname.lua`.
    - Legacy paths are still probed/written for compatibility, but the modern paths are preferred.

### Sprites and icons (GRF vs filesystem)

- `SpriteAssignmentService` is the primary way to install sprites and related textures:
  - Destination GRF: `TargetGrfPath` (if set) or `ClientRootPath/custom.grf`.
  - Handles:
    - Item/headgear `.spr`/`.act` at canonical item/accessory sprite paths.
    - Icons and collection art in `data/texture/유저인터페이스/item` and `.../collection`.
    - Optional effect textures and palettes.

- `ClientAssetWriter` creates **placeholder** icons as loose BMPs under:
  - `ClientRootPath/data/texture/유저인터페이스/item`.
  - `ClientRootPath/data/texture/유저인터페이스/collection`.
  - Intended for **LiveClient** mode to make local client testing easy when no real icons exist.
  - In **PatchOnly** mode, `App` does not construct `ClientAssetWriter`; workflows should instead
    rely on `SpriteAssignmentService` to install real icons into GRFs or patch directories.

### Modes summary (PatchOnly vs LiveClient)

- **PatchOnly (default, safer)**:
  - Server:
    - Read base DB from `db/re` / `db/pre-re` / `db`.
    - Write only to `db/import` (items, mobs, etc.).
  - Client:
    - Treat `ClientRootPath` and its `System/` scripts as read-mostly.
    - Prefer patch overlays under `ClientPatchRoot` (itemInfo_rodbeditor.lua, npcidentity/jobname overlays).
    - Install sprites/icons into a writable custom GRF using `SpriteAssignmentService`.
    - `ItemInfoLuaWriter` and `ClientAssetWriter` remain **disabled** in this mode.

- **LiveClient (opt-in)**:
  - Server: same as PatchOnly (all writes to `db/import`).
  - Client:
    - Allow direct writes to `ClientRootPath/System/itemInfo_C.lua` via `ItemInfoLuaWriter`.
    - Allow creation of placeholder icons as loose BMPs via `ClientAssetWriter`.
    - Still write sprites/accessory tables into a writable custom GRF.
  - This mode is convenient for a single developer/client but should be avoided for
    environments where the client install must stay clean and reproducible.

