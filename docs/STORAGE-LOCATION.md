# Changing M.I.S.T.'s storage location

M.I.S.T. stores its downloaded mod archives and backups in a configurable directory. This guide concerns **the archive storage directory**, not a Steam library or an installed game's files.

> **Preview warning:** Changing the configured path does **not** automatically migrate existing archives. Keep a backup, and do not delete the old directory until the new configuration is checked.

## Before changing anything

1. In **M.I.S.T. → Settings → Storage Location**, record the exact current archive path.
2. Back up the existing archive directory to another location. Preserve its names and contents.
3. Confirm that the intended new location is on a mounted, writable filesystem with sufficient free space.
4. Do **not** move, rename, or delete game installation files as part of this procedure.

## Change the setting

1. In **Settings → Storage Location**, choose **Change location**, select the destination and press **Save**.
2. Close M.I.S.T. completely before moving existing archives.
3. Move the existing **`.nx` archive files** from the previously recorded archive location to the newly selected archive location. Preserve their filenames; do not move unrelated profile/database files. Copying first and verifying before removing originals is safer than moving outright.
4. Start M.I.S.T., verify the configured archive path and confirm existing library items remain available before removing your old archive copies.

## If anything looks wrong

Close M.I.S.T.; do not repeatedly switch paths or manipulate the game directories. Retain the old archive copy and report the M.I.S.T. version, Linux distribution, the two archive paths (redact usernames if desired), and a relevant app log at [M.I.S.T. issues](https://github.com/shamenra/MIST/issues).

This is M.I.S.T.-specific preview documentation. [Original Nexus Mods App guidance](https://github.com/Nexus-Mods/NexusMods.App/blob/main/docs/users/faq/MoveLibraryLocation.md) is available as a credited upstream reference; behavior may differ in the fork.
