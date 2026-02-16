# Quick Start: Extracted Assets → Client (No Headache)

Use your **F:\MMORPG\EXTRACTED ASSETS** with **F:\MMORPG\RAGNAROK ONLINE\client** via RoDbEditor.

## Auto-Setup (Zero Config)

RoDbEditor auto-detects these paths when they exist:

- **Extracted Assets**: `F:\MMORPG\EXTRACTED ASSETS`
- **Client Root**: `F:\MMORPG\RAGNAROK ONLINE\client`
- **Patch Output**: same as client root

Just run RoDbEditor — no manual setup needed.

---

## Workflow: Extracted Assets → Client

### 1. Open RoDbEditor

- GRFs load from your client folder automatically.
- Extracted assets and client paths are auto-configured if folders exist.

### 2. Go to EXTRACTED ASSETS Tab

- Click the **EXTRACTED ASSETS** tab.
- Browse your extracted sprites (items, monsters, NPCs).

### 3. Assign an Asset to an Item/Monster/NPC

1. Select an extracted asset (e.g. a headgear sprite).
2. Choose **Entity Type**: Item, Monster, or NPC.
3. Pick the **target** (existing item/mob/npc or create new).
4. Click **Assign to custom.grf** or **Copy to Client Folder**.

### 4. Copy to Client Folder (Direct)

- **Assign Target**: choose "Copy to Client Folder".
- Files are copied into `F:\MMORPG\RAGNAROK ONLINE\client\data\...` with correct layout.
- For items: also saves to `itemInfo_C.lua`, `accessoryid.lua`, `accname.lua`, and icons.

### 5. Pack into custom.grf (Recommended)

- **Assign Target**: choose "custom.grf".
- RoDbEditor writes into `custom.grf` in your client folder.
- Ensure `custom.grf` is **first** in `DATA.ini` so it overrides official files.

---

## If Paths Differ

Use **File** menu:

- **Open Extracted Assets...** — point to your extracted assets root.
- **Open Client Folder...** — point to your client root.
- **Set Client Patch Output...** — where overlay files (System/, data/) are written.

Settings are saved to `RoDbEditor.ini` in AppData or next to the exe.

---

## Checklist for Items to Work In-Game

| Component | Where RoDbEditor Writes |
|-----------|-------------------------|
| Server DB | rAthena `db/import/item_db.yml` (via DataPath) |
| Item name/desc | `System/itemInfo_C.lua` or `SystemEN/itemInfo_C.lua` |
| Headgear view ID | `data/luafiles514/lua files/datainfo/accessoryid.lua` + `accname.lua` |
| Icons | `data/texture/유저인터페이스/item/*.bmp` |
| Sprites | `custom.grf` or `data/sprite/...` |

**Important**: If your client loads `itemInfo_true.lub` instead of `itemInfo_C.lua`, use **Tools → Write itemInfo_rodbeditor.lua** and ensure your client loads that overlay.
