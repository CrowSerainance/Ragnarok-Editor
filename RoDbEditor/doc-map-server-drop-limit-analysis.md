# Map-Server "Maximum of 10 monster Drops met, skipping" — Analysis

## What the error means

rAthena enforces `MAX_MOB_DROP = 10` items per mob. Any drop beyond the 10th is skipped.

## Root cause

When you put an override in `db/import/mob_db.yml`, rAthena **merges** the import entry with the base entry from `db/re/mob_db.yml`. Other fields (HP, stats, etc.) are replaced, but **Drops are merged**, not replaced.

So for Orc Warrior (1023):

- Base `db/re/mob_db.yml`: 8 drops  
- Import `db/import/mob_db.yml`: 8 drops  
- After merge: **16 drops**  
- rAthena: loads first 10, skips the rest (including Oridecon_Stone, Cigar, Battle_Axe_, Orcish_Axe, Round_Buckler, Orc_Warrior_Card)

The error lines (40, 42, 44, 46, 48, 50) match the import file’s drop entries.

## Reference

- [rAthena issue #6054](https://github.com/rathena/rathena/issues/6054): "Adding a copy of mob/item into import causes properties to merge"
- This behavior is documented; changing it would require rAthena code changes.

## Possible fixes (do not implement yet)

1. **Use Index to overwrite**  
   rAthena supports `Index` on drops. If you set indices 0–7 in the import, you might overwrite base slots instead of appending. Behavior here is not fully clear and may depend on rAthena version.

2. **Cap drops in RoDbEditor**  
   When saving to import, cap at 10 drops and warn if the base + import total would exceed 10.

3. **Drop-only override**  
   If you only want to change drops, you may need to avoid including a full mob override in import and instead use a different mechanism (e.g. script-based drops), depending on rAthena capabilities.

4. **Use base only**  
   Remove the Orc Warrior override from `db/import/mob_db.yml` so the server uses only the base entry (8 drops).

## Status

Analysis only. No RoDbEditor changes made for this issue yet.
