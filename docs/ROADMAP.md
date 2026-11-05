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
| Done locally | Steam artwork | Use locally cached Steam cover art with bundled fallback (MIST-006-07A) |
| In progress | Documentation & project links | M.I.S.T. README, roadmap, changelog, and GitHub buttons (MIST-006-08) |
| Planned | Linux OAuth packaging | Reliable `nxm://` callback handler registration for standalone AppImages |
| Planned | Additional game support | Move integrations through the acceptance pipeline above |
| Planned | Version coverage | Expand known historical Steam builds and public-version mappings |
| Planned | UI polish | Replace remaining inherited promotional/help links and inconsistent accent colors |
| Planned | Artwork preferences | Allow user-selected cover artwork with a safe fallback |
| Planned | Logging | Move future app log filenames to a M.I.S.T. prefix while retaining historic logs |
| Planned | Release process | Clear previews, release notes, validation, and reproducible AppImage smoke tests |

## Tracking progress

- **Roadmap:** Proposed priorities and validation criteria live in this file.
- **Supported games:** The [game tracker](SUPPORTED-GAMES.md) states what is actually tested.
- **Changelog:** [Meaningful completed changes](../CHANGELOG.md) are added during development.
- **Individual pushes:** The [GitHub commit history](https://github.com/shamenra/MIST/commits/mist/development) shows every committed change.
- **Requests:** [Open an issue](https://github.com/shamenra/MIST/issues/new) with the game name, storefront, Linux environment, and mod format.

The roadmap is maintained manually in the repository; it is not a live issue-board synchronization.
