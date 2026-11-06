# M.I.S.T. — Existing Mod Recognition (MIST-006-14B)

## Goal

Recognize installed mods from prior M.I.S.T. versions, Nexus downloads, and third-party/manual installations **without requiring a clean game directory**. A confirmed mod should eventually become a normal Library/loadout item. Only genuine unattributed changes should remain External Changes.

**Safety gate:** discovery ≠ deployment, adoption, removal, or cleanup. No existing mod gets management rights or deletion eligibility merely because we found its files.

## Implemented in 006-14B1 (experimental, read-only)

The `mist recognize-cyberpunk` diagnostic command accepts an explicit Cyberpunk game root, a directory of downloaded ZIP archives, and a JSON report destination outside the scanned directories. It:

1. Enumerates known Cyberpunk mod roots without following symlinked entries.
2. Examines ZIP entry destinations without extracting, writing to, or moving installed files.
3. Matches installed files to local archive entries by path **and SHA-256 content hash**.
4. Classifies exact-content, partial, path-conflict, and missing archive files, recording the original archive MD5 when a content match exists (for later Nexus API lookup).
5. Reads optional adjacent `.meta` JSON (`source=nexus`, `game=cyberpunk2077`, `modId`, `fileId`, `version`) as **unverified provenance hints**.
6. Lists unattributed installed files for later investigation.

No Nexus API calls, no library writes, no installation, no loadout synchronization. Archive sidecars are user/local data and can be inaccurate. Exact ZIP-content equality proves only equality with the local ZIP; it **does not** authenticate a Nexus download. Never automatically adopt based on this report.

This first adapter supports ZIP only, with bounded entry size and scan counts. 7z/RAR, nested archives, FOMOD installation choices, Vortex staging metadata, legacy M.I.S.T. database content-hash lookup, and mod bundles that transform files are not yet supported.

### Example command (recommended, isolated probe)

```bash
cd /mnt/FastStore/Projects/MIST/source
DOTNET_ROLL_FORWARD=Major dotnet run \
  --project tools/MIST.ModRecognitionProbe/MIST.ModRecognitionProbe.csproj \
  -c Release --no-restore -- \
  --game-root '/media/shamen/Games/SteamLibrary/steamapps/common/Cyberpunk 2077' \
  --downloads-root "$HOME/Downloads" \
  --output '/mnt/FastStore/Projects/MIST/builds/MIST-cyberpunk-recognition.json'
```

The standalone tool does not initialize M.I.S.T. application services, settings, CLI server, or MnemonicDB. The same engine is also registered as the `mist recognize-cyberpunk` command for future in-app integration, but the standalone tool is safer for first diagnostics.

The downloads location is illustrative. Choose the actual folder where Nexus mod archives live. Existing output files are **not overwritten**. The report lists local paths; review/redact before sharing.

## Next engineering stages

- **B2 — Library reconciliation:** Query the actual M.I.S.T. `LibraryArchiveFileEntry` and Nexus metadata models, matching hash/destination sets to installed files. Support cached Nexus download archives (including 7z/RAR), past app versions, and Vortex metadata. The existing library stores xxHash3 for extracted archive entries and MD5 for top-level archives; avoid assuming that an installed-file SHA-256 alone yields a Nexus ID.
- **B3 — Nexus identity verification:** Validate candidate game/mod/file identities through authenticated Nexus APIs, archive checksums, and/or known verified manifests. Separate trusted provenance, user-provided hints, and fuzzy names.
- **B4 — Review UI:** Group findings by mod, label exact/possible/unknown/conflict, compare versions/configs, and offer Keep Installed / Use Verified Nexus / Decide Later. Do not force per-file popups.
- **B5 — Safe library adoption:** Reconstruct installation manifests and ownership receipts, preserve reversible originals, perform revalidation at transaction time, and integrate approved mods into normal Library/loadout management. Do not grant ownership to unknown files. Honor MIST-006-14A unmanage/uninstall protections.

**Release guard:** Do not enable automatic adoption or cleanup until disposable-game integration tests prove preservation during upgrade, unmanage, loadout deletion, mod update, and app uninstall. This source-only diagnostic is not itself that guarantee.

## B2A — game-scoped grouping (2026-11-05)

The **default** `mist recognize-cyberpunk` command and the standalone
`MIST.ModRecognitionProbe` now invoke `MISTScopedCyberpunkDiscovery.Scan`.
They accept a Cyberpunk installation path and an output JSON path only;
`--downloads-root` was removed from the default diagnostic.

- The scope is only specifically enumerated mod-capable **subdirectories of the
  chosen game installation**. It does not search `~/Downloads`, another game,
  another Steam library, the home directory, or previous-manager staging areas.
- Discovery does **not** read original download archives. The older B1 ZIP
  matching utility remains code for future opt-in evidence providers but is
  not invoked by the default commands.
- Directory symlinks and linked intermediate components are skipped, preventing
  external-directory scanning through a symlink in a game installation.
- Exact normalized folder or file stems offer conservative candidate grouping;
  components from multiple game-relative locations can be grouped.
  Shared framework folders are not flattened into one mod; CET submods get
  separate groups. Matching tokens are clues, NOT definitive provenance.
- `.log` / `.sqlite3` files are *possibly generated* and `.json` / `.yaml`
  are *potentially mutable*. These are **hints**, not exclusion from the report.
- No Nexus source, version, author, package hash or license is verified at B2A.
  No game file is changed and no database / loadout management is initialized.
- Game binaries, generic config, and unrelated directories are intentionally
  not scanned. This first pass may miss mods placed outside known mod-capable
  locations. Expanding allowed roots requires a game adapter with clear rules.

**Do not use B2A group membership as permission to delete, disable, update,
remove, or adopt a file.** It is for recognition presentation and subsequent
identity verification, not file ownership.


## B2B1 — modular, game-local evidence enrichment (2026-11-05)

The read-only B2A Cyberpunk inventory now feeds `MISTModDiscoveryPipeline`, which runs
three independent evidence modules:

- `game-file-metadata`: inspects a bounded set of game-local version/readme/manifest
  text files (not logs, settings, user data, network, or old manager folders).
  Embedded version markers and Nexus URLs remain **unverified**; URLs can point to
  dependencies, not the mod itself.
- `selected-file-fingerprints`: computes SHA-256 for a single bounded, likely
  package file per candidate group. The hash is useful for later exact-release
  comparisons, but does not establish mod identity by itself.
- `component-relationship-hints`: proposes links between structurally related
  group names without merging their files or management records.

The standalone probe and diagnostic command now emit `v2b` with nested `inventory`,
`identityHints`, `contentFingerprints`, `possibleRelationships`, and `modules`.
Successful discovery never writes to game files, initializes the application
model, contacts Nexus, or grants management permissions. Module failures are
reported and do not erase the underlying B2A discovery.

**Future stage:** Reconcile local evidence with Nexus metadata (and, if the user
authorizes it, separately access older app provenance records). The filesystem
scan remains bounded to the game itself. Only confirmed evidence should be
converted to a precise Nexus mod ID + file/version claim.
