# M.I.S.T. — Game & Feature Roadmap

[← M.I.S.T. home](../README.md) · [Supported games](SUPPORTED-GAMES.md) · [Changelog](../CHANGELOG.md) · [Development commits](https://github.com/shamenra/MIST/commits/mist/development) · [Suggest a game or feature](https://github.com/shamenra/MIST/issues/new)

> **Living development plan — 2026-11-05.** This is an outline of intended work, not an announcement of support, a fixed schedule, or a guarantee that any given game will be added.

## Cadence and priorities

The *stretch goal* is roughly **two game-support additions or substantial game-validation milestones per week** when practical. Some games need deeper integration, format detection, safety analysis, or testing and may take longer. **Correctness and safe, reversible mod operations come before game counts.**

## Game support pipeline

| Track | Title / category | State | Next step |
| --- | --- | --- | --- |
| Existing | Cyberpunk 2077 (Steam) | Locally validated in M.I.S.T. | Keep regression coverage and improve version mapping |
| Existing | The Elder Scrolls IV: Oblivion Remastered (Steam) | Locally validated in M.I.S.T. | Broaden mod compatibility testing and safe-reversal coverage |
| Existing | Stardew Valley | Inherited integration; M.I.S.T. validation pending | Test Linux installation/discovery and mod workflows |
| Candidate | The Elder Scrolls V: Skyrim Special Edition | Proposed, **not yet verified** | Audit game layout, existing adapters, and safe mod paths |
| Candidate | Fallout 4 | Proposed, **not yet verified** | Audit game layout, existing adapters, and safe mod paths |
| Candidate | Community-requested games | Unscheduled | Triage issue reports by feasibility and test coverage |

See [SUPPORTED-GAMES.md](SUPPORTED-GAMES.md) for the current verified-versus-inherited distinction. Candidates are not promises.

### Acceptance criteria for each game

1. Locate a real installation without writing to its files.
2. Identify the correct game edition and storefront; identify the Steam App ID/build where applicable.
3. Confirm safe mod deployment paths and prevent changes to unrelated files.
4. Test loadout install, disable, re-enable, and removal against explicit file inventories.
5. Confirm modding-specific dependencies or limitations and record them in support docs.
6. Add meaningful regression tests and perform a local runtime smoke test before marking **validated**.

## Linux GOG discovery — MIST-006-10

**Status: Research / planned; Linux detection is unverified in M.I.S.T.** Some inherited GOG integrations (including Heroic) may already work in particular setups, but we do **not** claim reliable GOG discovery or mod deployment on Linux yet. The maintainer has no local GOG installations to validate against; community reports will be important. A discovered game is not automatically a safely supported game.

| Workstream | Status | Goal / evidence required |
| --- | --- | --- |
| Platform-aware GOG locator | Planned | Skip Windows registry (`HKEY_LOCAL_MACHINE`) lookup on Linux; retain existing Windows behavior. |
| Heroic GOG | Inherited / unverified locally | Inspect existing Linux discovery, including native and Flatpak paths; verify against real installs when possible. |
| Lutris GOG | Research | Read the Lutris installation database *read-only* and positively identify GOG-owned games; do not mistake unrelated Lutris games for GOG purchases. |
| Minigalaxy GOG | Research | Investigate configured paths and available installed-game metadata; avoid assuming a fixed library directory. |
| Native GOG / offline installers | Research | Detect reliable on-disk GOG identifiers with configurable library paths; distinguish Linux-native installs from Wine installs. |
| GOG game identity and versions | Planned | Normalize storefront IDs and installation paths; only display a version when supported by reliable installation metadata. |
| Safe mod deployment | Validation required | Never mark a newly detected GOG installation as fully supported without reversible deployment tests and verified writable paths. |

**Validation policy:** Label these capabilities **experimental / community-tested** only after a real implementation is present. Keep them **planned / unverified** until then. Accept community reports to establish which combinations work; avoid promising coverage for all GOG games or launchers. Target no fixed date.

**Community help wanted:** Please report successful detections as well as failures using the [Linux GOG report template](https://github.com/shamenra/MIST/issues/new?template=gog-linux-report.yml). Helpful details are the GOG game title and product ID (if known), Linux distribution, launcher and installation method, actual install directory, and M.I.S.T. build. Include reproducible behavior and relevant *redacted* log lines. **Never post login tokens, authorization URLs, or private account data.**

## Feature roadmap

| Status | Area | Planned outcome |
| --- | --- | --- |
| Planned | Manual game support | User-selected game installation path with read-only validation and safe supported-adapter gating (MIST-006-13) |
| Planned / research | Recognition of existing Nexus mods | Read-only inventory and evidence-backed Nexus matches; user-confirmed adoption only (MIST-006-13) |
| Done locally | Steam artwork | Use locally cached Steam cover art with bundled fallback (MIST-006-07A) |
| In progress | Documentation & project links | M.I.S.T. README, roadmap, changelog, and GitHub buttons (MIST-006-08) |
| Planned | Linux OAuth packaging | Reliable `nxm://` callback handler registration for standalone AppImages |
| Planned | Additional game support | Move integrations through the acceptance pipeline above |
| Planned | Version coverage | Expand known historical Steam builds and public-version mappings |
| Planned | UI polish | Replace remaining inherited promotional/help links and inconsistent accent colors |
| Planned | Artwork preferences | Allow user-selected cover artwork with a safe fallback |
| Planned | Logging | Move future app log filenames to a M.I.S.T. prefix while retaining historic logs |
| Planned | Release process | Clear previews, release notes, validation, and reproducible AppImage smoke tests |

## Manual game support and existing Nexus mod recognition — MIST-006-13

Both capabilities are **planned, not yet implemented**. Detection and identification must remain separate from permission to modify a game installation.

### Manual game support — planned

- Let a user select an existing game installation directory if Steam, GOG, Heroic, Lutris, or another automatic locator misses it. Preserve storefront and edition details when known; allow an explicit *unknown storefront* state rather than guessing.
- When the game already has a supported M.I.S.T. adapter, validate the selected directory read-only against that adapter (expected game files, edition, known mod paths, and writable boundaries) and display a preview before enabling mod management.
- For an unknown or unsupported title, allow a **catalog-only/manual entry** without claiming deployment support. A new game-specific adapter and safety tests are still required before enabling install/remove operations.
- Store the manual path as user-configured metadata; support multiple installations, removable drives, changed locations, and a safe recheck if files disappear. Do not silently redirect a missing installation.
- Acceptance: a valid existing installation can be added and reopened; an invalid or unrelated directory is rejected without modification; manual discovery never bypasses receipt-based reversal and original-file protection.

### Recognize already-installed Nexus mods — planned / research

- Add a **read-only existing-mod inventory** for supported games, starting with Cyberpunk 2077's known mod locations (for example archive, RED4ext, CET, redscript, and related content when relevant). Never assume every file in a mod directory belongs to one mod.
- Where available, compare locally installed filenames, hashes, archive manifests, known Nexus Mod IDs/file IDs, installer metadata, and user-provided download records against reliable Nexus metadata. A common filename or similar title alone is **not proof of a match**.
- Label results **Confirmed**, **Possible — needs confirmation**, or **Unknown/local mod**; show which evidence supports each identification and let users correct it. Preserve non-Nexus, manually installed, and custom mods.
- Offer an explicit **adopt/import into management** operation only after the user reviews the detected files and potential conflicts. Existing files remain untouched until the user chooses a safe action; do not auto-delete, replace, or enable anything during inventory.
- Model ownership/provenance carefully: adoption needs an auditable, per-file baseline/receipt, conflict handling, and rollback behavior that cannot erase files M.I.S.T. did not deploy. For uncertain provenance, prefer **track without taking ownership** until a safe migration design is validated.
- Acceptance: read-only scan causes zero changes to game files; matches are evidence-backed; unknown/modified files remain visible; confirmed and adopted mods survive rescans and loadout switches without data loss.

See [supported-game validation](SUPPORTED-GAMES.md) before interpreting either planned feature as universal mod-management support.

## Tracking progress

- **Roadmap:** Proposed priorities and validation criteria live in this file.
- **Supported games:** The [game tracker](SUPPORTED-GAMES.md) states what is actually tested.
- **Changelog:** [Meaningful completed changes](../CHANGELOG.md) are added during development.
- **Individual pushes:** The [GitHub commit history](https://github.com/shamenra/MIST/commits/mist/development) shows every committed change.
- **Requests:** [Open an issue](https://github.com/shamenra/MIST/issues/new) with the game name, storefront, Linux environment, and mod format.

The roadmap is maintained manually in the repository; it is not a live issue-board synchronization.
