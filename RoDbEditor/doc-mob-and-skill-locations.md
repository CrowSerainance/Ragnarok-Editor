# RoDbEditor: Where mob_db and mob_skill Live (client vs rAthena)

## Short answer

- **All edits and additions** (Add Monster, Edit Monster, Add/Edit Slave, Add/Edit Skill) are written **only to rAthena (server)** under the **DataPath** folder.  
- **Nothing is written to the client** (GRF). The client is only **read** for display/fallback (sprites, iteminfo.lub, mobinfo.lub when server DB has no data).

---

## DataPath = rAthena (server) root

- **Source:** `RoDbEditorConfig.DataPath`  
  - Loaded from `RoDbEditor.ini` → `[GRF]` → `DataPath=...`  
  - Hard default in code: `F:\MMORPG\RAGNAROK ONLINE\rathena-master` (when that folder exists).
- **Meaning:** This path is the **rAthena repo root** (server). All server DB files are under `{DataPath}\db\...`.

---

## mob_db (monsters)

| What                | Location in code              | Physical path (under DataPath)        | Client or rAthena? |
|---------------------|-------------------------------|----------------------------------------|--------------------|
| **Read (load)**      | `MobDbService.LoadFromDataPath` | `db/re/mob_db.yml` → `db/pre-re/mob_db.yml` → `db/mob_db.yml` (first existing) | rAthena (server)   |
| **Fallback read**   | `App.LoadMobsFromGrf()`       | Inside GRF: `data\...\mobinfo.lub`     | Client (read-only) |
| **Write (save)**    | `MobDbService.SaveMob`        | **Always** `db/import/mob_db.yml`      | rAthena (server)   |

- **Insert/Edit target:** `{DataPath}\db\import\mob_db.yml`  
  - Add Monster and Edit Monster both write here. Base `db/re/mob_db.yml` is never modified.

---

## mob_skill (skills/slaves)

| What                | Location in code                    | Physical path (under DataPath)        | Client or rAthena? |
|---------------------|-------------------------------------|----------------------------------------|--------------------|
| **Read (load)**     | `MobSkillDbService.LoadFromDataPath` | `db/import/mob_skill_db.txt` then `db/re/mob_skill_db.txt` then `db/pre-re/mob_skill_db.txt` then `db/mob_skill_db.txt` (all existing files merged) | rAthena (server)   |
| **Write (save)**    | `MobSkillWriteService`             | **Always** `db/import/mob_skill_db.txt` | rAthena (server)   |

- **Insert/Edit target:** `{DataPath}\db\import\mob_skill_db.txt`  
  - Add Slave, Edit Slave, and any mob skill edits go here. Base `db/re/mob_skill_db.txt` is never modified.

---

## Summary table (where it inserts edits and additions)

| Operation           | File used for insert/edit        | Under folder              | Client or rAthena? |
|--------------------|-----------------------------------|---------------------------|--------------------|
| Add Monster        | `mob_db.yml`                      | `db/import/`              | **rAthena**        |
| Edit Monster      | `mob_db.yml`                      | `db/import/`              | **rAthena**        |
| Add Slave / Skill  | `mob_skill_db.txt`                | `db/import/`              | **rAthena**        |
| Edit Slave / Skill | `mob_skill_db.txt`                | `db/import/`              | **rAthena**        |

- **Client (GRF):** Only **read** for items/mobs when server has no DB, and for sprites/assets. **No writes to client.**

---

## Code references (F:\2026 PROJECT\ROMapOverlayEditor\RoDbEditor)

- **Config / DataPath:** `Config\RoDbEditorConfig.cs` (DataPath, DefaultDataPath).
- **mob_db read:** `Services\MobDbService.cs` → `LoadFromDataPath` (db/re, db/pre-re, db).
- **mob_db write:** `Services\MobDbService.cs` → `SaveMob`, `EnsureImportMobDbPath` → `db/import/mob_db.yml`.
- **mob_skill read:** `Services\MobSkillDbService.cs` → `LoadFromDataPath` (import, re, pre-re, db).
- **mob_skill write:** `Services\MobSkillWriteService.cs` → `ImportFilePath` = `db/import/mob_skill_db.txt`; `AppendSkillRow`, `UpdateSkillRow`, `DeleteSkillRow`.
- **Server vs client wording:** `App.xaml.cs` → “Reload server data from rAthena DataPath”; “GRF” used only for fallback load and assets.
