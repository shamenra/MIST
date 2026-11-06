# M.I.S.T. — Game & Feature Roadmap

[← M.I.S.T. home](../README.md) · [Supported games](SUPPORTED-GAMES.md) · [Changelog](../CHANGELOG.md) · [Development commits](https://github.com/shamenra/MIST/commits/mist/development) · [Suggest a game or feature](https://github.com/shamenra/MIST/issues/new)

> **Living development plan — 2026-11-06.** This describes priorities, designs, prototypes, and validation gates; it is **not** a release promise or a claim that every listed feature works. Implementation status is based on local M.I.S.T. work and the 2026-11-06 read-only source audit, not an independently verified release build. See [SUPPORTED-GAMES.md](SUPPORTED-GAMES.md) for per-game claims.

## Mission and design rules

**M.I.S.T. (Mod Integration and Sorting Technology) is the traffic cop:** it helps people discover, understand, organize, and use mods with as little friction as possible. Its job is to direct changes, **not** to assume ownership of files it happens to find.

- **M.I.S.T. owns the experience.** Game registration should be quick, understandable, and usable without unexplained multi-minute *Collecting files* states or alarming *dirty/unclean installation* prompts. Actual file-changing operations still require appropriate safety information and authorization.
- **An existing modded installation is normal.** A clean directory, original download archive, or former manager's database is **not** a prerequisite to recognizing a game or its mods.
- **One authoritative inventory, many consumers.** A M.I.S.T.-owned File Intelligence Engine supplies observations to game identification, mod discovery, reconciliation, version evidence, and—after explicit authorization—the management system. Avoid duplicate broad scans of the same installation.
- **Fast and lightweight by default.** Read paths, structure, sizes, timestamps, and relevant local metadata first. Hash selected files only to answer a concrete question. Do not hash entire installations just to register games.
- **Accurate without guessing.** Distinguish *detected*, *probable*, *verified*, *unknown*, and *not yet scanned*. Do not claim a Nexus identity or installed version from a filename alone.
- **Game support is game-specific.** Ship small, declarative local definitions researched for **each title**. Do **not** inherit mod acceptance, folder locations, or deployment rules merely because two games share an engine or developer. Reuse generic file utilities, not assumed game semantics.
- **Observation never grants ownership.** Discovery, identification, and tracking do not authorize modification, adoption, synchronization, removal, or cleanup. Unknown files must remain untouched.

See also **MIST-ADR-001 — Game Onboarding, File Intelligence, and Safe Management** (accepted design, implementation pending; proposed for addition to the repository).

## Priority order and status

Statuses deliberately distinguish a **local prototype** from a **validated end-to-end capability**.

| Priority | Workstream | Status (2026-11-06) | Next deliverable / gate |
| --- | --- | --- | --- |
| **P0** | Legacy collector and synchronization contract audit — **MIST-006-14B2B9** | **Static audit completed; runtime profiling pending** | Map the game-add call chain, clean-directory dialog, database contracts, hashing, and authorization boundaries; measure latency, CPU, and disk I/O without destructive test resets. |
| **P0** | M.I.S.T. File Intelligence Engine | **Design accepted; not implemented as a shared service** | Small, read-only, metadata-first inventory contract with incremental caching, resource budgets, cancellation, and game-defined search rules. |
| **P0** | Automatic recognition of pre-existing mods — **MIST-006-14B** | **Active local Cyberpunk prototype; incomplete** | Move discovery and conservative reconciliation into the shared inventory pipeline; strengthen identities, versions, and cross-scan persistence. |
| **P0** | Game-add flow and first-run experience | **Planned / designed** | Let users register games immediately and evaluate installations without requiring a clean baseline or running synchronization as an implicit precondition. |
| **P0** | Synchronization safety and ownership | **Guards developed; end-to-end regression pending** | Preserve mutation guards, receipts, quarantine/recovery and original-file protection while isolating expensive legacy bookkeeping from initial discovery. |
| **P1** | Official per-game definition database | **Draft Cyberpunk definition; schema/loader pending** | Versioned, local, declarative definitions; no engine-profile inheritance. |
| **P1** | Game/version identification | **Steam discovery/build research in progress** | Prefer reliable local storefront/build metadata; verify ambiguities selectively. |
| **P1** | Prior-manager migration | **Planned / designed** | Read-only Vortex/MO2 (and other manager) metadata adapters, reconciled with independent disk observations; user chooses which games to register. |
| **P2** | User-defined unsupported games — **MIST-006-13** | **Planned; broadened design** | Let users define game path, executable, mod destination(s), and extract-versus-keep-archive behavior under safe boundaries. |
| **P2** | Linux GOG discovery — **MIST-006-10** | **Research / unverified** | Validate real Linux installations and launcher variants; preserve source/edition identity. |
| **Later** | Archive retention — **MIST-006-15 (provisional)** | **Planned** | Optional, user-controlled source archive backups and cleanup safeguards. |

**Scheduling:** Approximately two game-support additions or significant validation milestones per week remains an **aspirational stretch goal**, not a deadline. Safety and correctness take precedence over counts.

## User journey: first launch to a usable Library

### One-time onboarding — planned

Store a **completed onboarding state**, not a flag set merely because the welcome dialog appeared. If setup is interrupted, it can resume. Migration should also be available later from Settings.

1. Welcome: **“Thank you for downloading M.I.S.T. — Mod Integration and Sorting Technology!”**
2. Choice: **Start Fresh** or **Merge from another Mod Manager**.
3. Both paths perform independent, **read-only game discovery** (Steam first; GOG and other storefront coverage as validated).
4. Show recognizable supported games with installation locations, plus a **separate unsupported-games list**. Ask which supported games to add; *No* leaves them available to add later.
5. Register selected games quickly, even if mod detection and deeper identity checks are not finished. Do not silently grant the application ownership of existing files.

**Start Fresh** means *no manager-metadata import*, **not** *assume the installed games are unmodded*.

**Merge** first discovers other managers' locally available records—configured games, paths, profiles, mod IDs, versions, enabled/disabled states—and compares them with independent game/mod discovery. Existing manager records are evidence, not proof that the files are currently deployed, nor authorization to manage them. The user still selects which games M.I.S.T. should add. Migration does not automatically remove, import, or redeploy files.

### Adding a game — target UX

```text
Click Add Game / select games during onboarding
  → Register game(s) and open Library promptly
  → Evaluating installation       [lightweight; bounded metadata scan]
  → Identifying modifications     [game-specific rules and grouping]
  → Library shows known, probable, unknown, and pending items
  → Checking versions            [selective local evidence / Nexus where available]
  → Optional later management action, explicitly authorized
```

Use understandable statuses and visible progress where meaningful. No unexplained warning solely because files were installed by hand or another manager. **Adding a game is not the same as permitting installs, removals, or synchronization.** Do not hide a large, resource-intensive operation in the background; throttle, defer, or pause it when appropriate.

## M.I.S.T. File Intelligence Engine — next architecture milestone

**Goal:** one M.I.S.T.-owned, game-definition-driven observation system. The user sees one coherent installation evaluation; internal stages are progressive and can update the Library without blocking initial registration.

| Stage | Required output | Cost / safety constraint |
| --- | --- | --- |
| Game identity | Title, installation, storefront, edition, credible game build or *unknown* | Prefer storefront/manifests and bounded read-only checks. |
| Initial inventory | Paths, sizes, timestamps, mod-root structure, errors/skipped locations | Targeted traversal of **explicitly configured** locations; no mandatory full-game hashing. |
| Mod discovery | Recognizable files/containers and probable boundaries | Local rules, manifests, loader declarations; avoid blindly treating folders as one mod. |
| Reconciliation | Conservative cross-directory grouping with path accounting and evidence | Every observation accounted for exactly once; no speculative merges. |
| Identity/version enrichment | Nexus/local metadata candidates, confidence, version evidence | Offline baseline works; network requests are optional, bounded, cached, and non-blocking. |
| Incremental tracking | Changed/removed/added observations; incomplete-scan signals | Cache metadata and scoped fingerprints; recheck when evidence is stale. |
| Pre-mutation verification | Current state of **affected** paths, ownership receipts, recoverability | Fresh validation immediately before an authorized write/delete/replace; fail closed. |

**Game definition storage:** ship a small versioned local database of independently researched game entries. Each definition may specify game/store IDs, executable/build evidence, mod search roots, allowed file/package formats, loaders, exclusions, manifests, destination mapping, installation mode, and known limitations. Data remains declarative; no arbitrary executable scripts in definitions. A game's modding rules must be explicitly verified, regardless of engine similarity.

**Performance contract:** registration must not wait for full-installation content hashes. Heavy tasks have bounded I/O, CPU, network, and concurrency; pause/cancel/resume and incomplete-state reporting are design requirements. Benchmark one game and bulk registration of **15 games sized roughly 40–150 GB each**. Measure time to registration/first useful Library, disk bytes read, and responsiveness **before** declaring any speed target met. No unmeasured performance claims.

### Legacy collector investigation — MIST-006-14B2B9

**Static source audit (2026-11-06):** The read-only audit scanned **1,853 source files**, about **6.8 MB** of source, and recorded **1,152 matching source lines**. It pointed to `GameLocationsService.cs`, `ALoadoutSynchronizer.cs`, and related file-hash, database, and synchronization code. These are **source search matches**, not executed-call counts or a performance profile. The audit's `runtime.status` was **`not-requested`**.

Source review indicates that the legacy indexer records files individually, compares size/timestamp against saved state, and can hash new/changed files; the synchronizer's **“Collecting files”** status precedes building and executing a sync plan. This is **not** evidence that content hashing caused the observed ~2–3-minute add-game delay. We still need to identify where the time goes and which operations truly belong on registration.

**Next gates:** trace the real game-add path; locate and redesign the clean-directory/external-file prompt; determine which disk-state and original-file protections remain essential; profile a natural scan with elapsed time and disk I/O; classify each dependency as **keep / adapt / isolate / remove**. Do **not** delete legacy functionality or bypass mutation guards merely to improve the progress indicator.

## Existing mod recognition — MIST-006-14B

**Current evidence, not finished product:** The local Cyberpunk discovery prototype finds mod-related paths without relying on original ZIP downloads. A recent read-only run accounted for **478 observations**: **477** associated with **28 provisional structural groups**, plus **1** unassociated file. Conservative offline reconciliation proposed **25 mod entries plus one review-only unassociated-files entry**, making only two strong cross-group combinations. These are **proposals**, not proof that all mods or installed versions are correct.

The native Library has displayed discovered entries with a read-only **Manage** affordance. A fresh game registration displayed **28 groups** rather than the previously reconciled 26 rows, because the reconciliation overlay was tied to an earlier discovery snapshot. **Automatic cross-scan reconciliation, reliable version verification, and adoption into management are not complete.** A corrected overlay build's final runtime behavior has not yet been confirmed here.

### Implementation sequence

1. **Discovery:** use explicit game-defined locations and local metadata; preserve unknown files and report unscanned/failed roots. No game-file writes.
2. **Reconciliation:** join files spanning multiple folders conservatively; retain original paths, group boundaries, provenance, and independent unattributed entries. Never infer ownership from grouping.
3. **Identity candidates:** use local manifests, optional former-manager records, trusted Nexus metadata, mod/file IDs, and release-specific evidence. Keep ambiguous candidates visible; do not use name similarity as proof.
4. **Version verification:** read local version evidence where trustworthy, selectively hash distinguishing files or compare known release artifacts, and show *unknown* when unresolved. Avoid global hashing or unconditional package downloads.
5. **Unified Library:** render discovered mods alongside managed items in a consistent presentation; label *Unmanaged / Identified / Version unverified* accurately. A read-only details/Manage view must not imply installation/removal authority.
6. **Safe tracking and optional later adoption:** rescan idempotently and update evidence. Adoption requires an explicit user decision plus per-file ownership receipts, collision review, validated originals/rollback, and recovery guarantees. Never silently adopt, uninstall, or rewrite a discovered mod.

**Acceptance:** same installed files produce stable results across scans; zero paths lost/duplicated; no dependence on an old report's exact filename; Nexus downtime does not block Library; unknown/modified files remain visible; rescanning makes no game-file mutations; ambiguous matches are not presented as verified versions.

## Game support pipeline

**Two distinct claims:** *M.I.S.T. can detect this installation* and *M.I.S.T. has validated safe mod deployment for this exact game/store/OS* are **not** interchangeable. Do not mark a title fully validated without install, disable, re-enable, remove, rollback, and original-file safety tests.

| Track | Game / category | Current evidence | Next validation |
| --- | --- | --- | --- |
| Active local test | **Cyberpunk 2077 (Steam)** | Steam discovery and mod-location inventory exercised locally; Library display prototype and version-evidence experiments. **Full safe-management certification pending.** | Game-specific definition, stable reconciliation, identity/version tests, file-operation regression. |
| Safety regression | **The Elder Scrolls IV: Oblivion Remastered (Steam)** | Installation discovery exercised; an earlier synchronizer incident deleted essential original game content. Protective guards were developed afterward. **Do not describe end-to-end behavior as fully validated.** | Prove original-file protection and safe loadout operations in disposable/test installations before broad claims. |
| Inherited integration | **Stardew Valley** | Upstream integration exists; M.I.S.T.-specific Linux validation pending. | Test installation detection, mod paths, install/removal, and rollback. |
| Candidate | **Skyrim Special Edition** | Proposed, not yet validated for M.I.S.T. | Research and test its own modding rules and safe paths; no automatic Creation Engine assumptions. |
| Candidate | **Fallout 4** | Proposed, not yet validated for M.I.S.T. | Independently research and test game-specific modding/deployment behavior. |
| Future | **Community-requested games** | Unscheduled | Triage by available evidence and reproducible validation. |

### Per-game acceptance criteria

1. Detect a real installation, storefront and edition **read-only**; verify build from credible evidence or report *unknown*.
2. Publish an independent game definition, with explicit supported mod paths/formats and unsupported cases; no engine-family shortcuts.
3. Distinguish scanning coverage, detected mod evidence, and remaining unknowns; prove clean and already-modded installs are both acceptable.
4. Verify install/disable/re-enable/removal and collision/rollback behavior on test fixtures or disposable installs, with receipts and original-file protections.
5. Test Linux/Windows cases actually claimed, including permissions, symlinks, paths, and launcher integration.
6. Add automated regression coverage and a real runtime smoke test **before** changing the title's verified support label.

## User-defined games — MIST-006-13 (planned)

For games missing automatic support, allow a user-configured profile rather than restricting them forever to a catalog-only entry. The user will define **game directory, executable, one or more mod destinations, and whether downloads are extracted or retained in an archive (e.g. ZIP)**. Provide a reviewable mapping/preview, sandboxed path validation, overwrite/collision protections, and explicit authorization before writing files.

This is **generic, user-directed management**, not a promise that M.I.S.T. can automatically understand arbitrary loaders, executable patchers, SQL/Lua runtime hooks, specialized installers, or load-order semantics. Clearly label unsupported operations and limitations. A manually selected path to an **officially defined game** can use that game's independently validated definition after verification. Keep user definitions separate from signed/verified official definitions, and support multiple installs, moved drives, and safely missing paths.

## Migration from previous mod managers — planned

- Identify available manager metadata **read-only** (Vortex and Mod Organizer 2 are initial research candidates, not implemented compatibility claims).
- Reconcile reported installations, profiles, enabled states, mod/version IDs, and source records with game discovery; do not equate a manager record to a physically deployed mod.
- Treat imported identity as supporting evidence; preserve disabled, missing, edited, and custom mods without changing them.
- Offer per-game user choice to register with M.I.S.T.; migration remains available after first-run onboarding.
- Require separate, explicit and verifiable authorization for any future adoption or handover of file control.

## Linux GOG discovery — MIST-006-10

**Status: Research / planned; reliable Linux discovery and deployment are not yet locally verified.** Some inherited GOG/Heroic capabilities may work in particular environments; a discovered title is not automatically certified for safe management.

| Workstream | Status | Required evidence |
| --- | --- | --- |
| Platform-aware GOG locator | Planned | Skip Windows registry lookup on Linux; retain Windows behavior. |
| Heroic GOG | Inherited / unverified locally | Test native and Flatpak discovery with real installations. |
| Lutris GOG | Research | Read local database without writes; positively identify GOG installations. |
| Minigalaxy GOG | Research | Examine actual metadata/configured library locations. |
| Native GOG / offline installers | Research | Reliable on-disk identity; distinguish native Linux and Wine installs. |
| GOG game identity/builds | Planned | Normalize product/build IDs; report unknown rather than guess. |
| Deployment support | Requires validation | Independently test writable paths, mod rules, reversibility, and original-file protection. |

**Community validation:** report successful detections *and* failures using the [Linux GOG report template](https://github.com/shamenra/MIST/issues/new?template=gog-linux-report.yml). Include title/product ID if known, distro, launcher, actual install method/path and M.I.S.T. build; redact tokens, authorization URLs and private information. Do not advertise experimental coverage until an implementation has been tested.

## Later features and maintenance

| Area | Status | Scope |
| --- | --- | --- |
| **Optional source-archive retention — MIST-006-15 (provisional)** | Planned / later | User chooses whether to preserve downloaded ZIP/7z/RAR archives, where to store them, storage limits, and explicit cleanup. Turning retention off must not silently remove existing backups or the last recoverable copy of user content. |
| **Manual mod identification and grouping** | Planned / later | Let users label unknown files and correct probable groups without claiming ownership. |
| **Steam artwork — MIST-006-07A** | Implemented locally | Cached Steam cover art with bundled fallback; broader art customization planned. |
| **Documentation and project links — MIST-006-08** | In progress | Keep README, roadmap, supported-games tracker and changelog consistent with implemented vs planned features. |
| **Linux OAuth / `nxm://` packaging** | Planned | Reliable callback registration for standalone AppImages. |
| **Game build coverage** | In progress / expansion planned | Historical Steam builds and credible public version mappings. |
| **UI polish** | Planned | Consistent M.I.S.T. labels, interactions, accent colors and help links; distinguish verification from management. |
| **Artwork preferences** | Planned | User-selected cover artwork, with a safe fallback. |
| **Logging** | Planned | M.I.S.T.-prefixed new logs, preserve access to legacy logs, redact secrets. |
| **Release process** | Planned | Repeatable builds, AppImage smoke tests, release notes and documented validation matrix. |

## Safety and performance release gates

- **Zero mutation during observation.** First-run, game discovery, mod identification, manager-metadata inspection, reconciliation and version lookup must not modify game files or confer ownership.
- **No implicit sync on Add Game.** If old bookkeeping must temporarily remain, isolate and profile it; do not silently perform file-changing work during registration.
- **Positive ownership and recovery proof before mutation.** Revalidate affected files; verify scope, receipts, collision and recoverability. Missing baseline, missing originals, stale state or unknown provenance must **fail closed**, never become a deletion signal.
- **No full-install hashing by default.** Instrument time-to-useful-Library, bytes read, CPU/I/O use and concurrent-load responsiveness; test 1 and 15-game cases before asserting a speed improvement.
- **Stable inventories.** Account for every observed path; distinguish *no mods*, *not scanned*, and *scan incomplete*. Reject stale reconciliation evidence without losing visibility of unverified groups.
- **Release claims match test scope.** Locally displayed results, successful compilation, a source audit, and verified reversible management are separate milestones.

## Tracking progress

- **Roadmap:** Intent, milestones, evidence and validation gates live here.
- **Supported games:** [SUPPORTED-GAMES.md](SUPPORTED-GAMES.md) states the precise tested/untested combinations; update it alongside any support-state change.
- **Changelog:** [CHANGELOG.md](../CHANGELOG.md) records meaningful implemented changes, not future designs.
- **Commits:** [mist/development history](https://github.com/shamenra/MIST/commits/mist/development) records actual repository changes.
- **Requests:** [Open an issue](https://github.com/shamenra/MIST/issues/new) with the relevant game, storefront, OS, mod format, and reproducible details.

This roadmap is maintained manually and is not a live issue-board synchronization. **Planned** features and prototype evidence must not be described as shipped functionality.
