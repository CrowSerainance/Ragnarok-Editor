# Workspace Profile Switching – Manual Test Checklist

Implementation is complete. Please run the following checks to validate.

## 1. Profile switching (no restart)
- Create 2 profiles (FILE ASSIGNMENT tab: New Profile, set different rAthena and client roots per profile).
- Switch A → B → A using the profile dropdown and **Apply**.
- Verify:
  - Source counts (items/mobs) and source indicators update.
  - Sprite preview uses the active client data.
  - Writes go to the active profile’s target paths.

## 2. Restart persistence
- Set a profile and paths, then close the app.
- Reopen and confirm the last active profile and paths are restored.

## 3. Legacy config
- Rename or remove `%AppData%\RoDbEditor\RoDbEditor.ini` and create a minimal legacy INI with only `[GRF]` and keys: `DataPath`, `ClientRootPath`, `Path=...`.
- Start the app and confirm it loads and creates a default profile from those keys.

## 4. DataPath persistence
- File → Select rAthena folder; choose a folder. Restart and confirm the path is still set.

## 5. Client folder hot-switch
- Tools → Open Client Folder; choose a different client. Confirm sprite sources and writers update without restart (e.g. item/mob counts and previews).

## 6. FILE ASSIGNMENT Set buttons
- In FILE ASSIGNMENT, use **Set…** for RAthena, Client root, and Patch output. Confirm paths update and Apply refreshes the workspace.
