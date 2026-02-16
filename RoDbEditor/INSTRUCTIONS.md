# RoDbEditor Instructions: Custom Items and GRF Structure

## data.grf vs custom.grf

- **data.grf** — Official game data. RoDbEditor does **not** edit it directly.
- **custom.grf** — For your custom assets. RoDbEditor writes sprites and icons here.

Your client must load **custom.grf first** in `DATA.ini` so it overrides `data.grf`:

```ini
[Data]
0=custom.grf
1=data.grf
2=...
```

If `data.grf` is first, your custom files are ignored.

---

## Where Item Data Comes From

| Source | Purpose |
|--------|---------|
| **Server** | `db/import/item_db.yml` (custom) and `db/re/item_db_equip.yml` (official) |
| **Client** | `System/itemInfo_C.lua` — names and descriptions |
| **Assets** | Icons and sprites in GRFs (`custom.grf`, `data.grf`) |

RoDbEditor writes to:
- Server: `db/import/item_db.yml`
- Client: `System/itemInfo_C.lua`, `accessoryid.lua`, `accname.lua`
- Assets: `custom.grf` (or `TargetGrfPath` in config)

---

## Korean Folder Structure Reference

| Folder | Korean | Purpose |
|--------|--------|---------|
| 남 | Male | Male sprite folder |
| 여 | Female | Female sprite folder |
| 로브/남 | Robe Male | Male costume (robe) sprites |
| 로브/여 | Robe Female | Female costume (robe) sprites |
| 아이템 | Item | Drop item sprite, general item .act/.spr |
| 유저인터페이스 | User Interface | Inventory/Collection texture folder |
| 유저인터페이스/item | Item icon | Inventory icon (24x24 .bmp) |
| 유저인터페이스/collection | Collection | Right-click detail view icon |
| sprite/몬스터 | Monster | Monster .act/.spr |
| sprite/아이템 | Item | Item .act/.spr |
| sprite/악세사리/남 | Accessory Male | Headgear male |
| sprite/악세사리/여 | Accessory Female | Headgear female |

---

## Step-by-Step: Add a Custom Item

### Path A: New item from scratch

1. **Run RoDbEditor** — GRFs auto-load from the client folder.
2. **ITEMS tab** → **+ NEW** — Creates a new item with the next free ID.
3. **Edit fields** in the right panel:
   - **AegisName** — Internal name (e.g. `Custom_Headgear_01`)
   - **Name** — In-game display name
   - **Type** — e.g. `Armor` for headgear
   - **Locations** — Equip slots (e.g. Upper Headgear)
   - **View** — Headgear sprite ID (must match `accessoryid`)
   - **ResourceName** — Sprite base name (e.g. `MyHeadgear`)
   - **Description** — Custom lore (multi-line, supports `^000000` color codes)
4. **SAVE** — Writes to server DB, client itemInfo, accessoryid, and placeholder icons.
5. **Add sprites** — Put `.spr`, `.act`, and `.bmp` into `custom.grf` or use EXTRACTED ASSETS → Assign to custom.grf.

### Path B: New item from extracted sprite

1. **EXTRACTED ASSETS tab** — Browse your extracted sprites.
2. **Select a sprite** (e.g. headgear).
3. **Entity Type** → **Item**
4. **Assign to** → **Create new item** (or pick an existing one).
5. **Assign to custom.grf** — Copies sprites into `custom.grf` and creates/updates the item.
6. **ITEMS tab** — Open the new item, adjust stats and **Description** (lore).
7. **SAVE**

### Path C: Edit an official item (name, description, stats)

1. **ITEMS tab** — Search for the official item.
2. **Edit fields** including **Description** for custom lore.
3. **SAVE** — Writes to `db/import/item_db.yml` (overlay; does not modify `item_db_equip.yml`) and `itemInfo_C.lua`.

---

## Checklist: Item Works In-Game

| Component | Where |
|-----------|-------|
| Server DB | `rathena-master/db/import/item_db.yml` |
| Item name/desc | `client/System/itemInfo_C.lua` |
| Headgear view ID | `accessoryid.lua` + `accname.lua` |
| Item icon | `data/texture/유저인터페이스/item/<ResourceName>.bmp` |
| Collection icon | `data/texture/유저인터페이스/collection/<ResourceName>.bmp` |
| Sprite | `custom.grf` at `data/sprite/아이템/` or `data/sprite/악세사리/남`, `여` (headgear) |

---

## Editing Item Description (Custom Lore)

- In the **ITEMS** tab, the **Description** field is editable.
- Enter multi-line text; use `^000000` style color codes if your client supports them.
- **SAVE** writes it to `itemInfo_C.lua` as the identified description.
- If you leave Description blank, stats are auto-generated from item properties.
