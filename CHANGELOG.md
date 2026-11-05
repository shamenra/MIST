# M.I.S.T. — Changelog

This is the **M.I.S.T. project changelog**. It records meaningful user-facing development changes and releases, not every commit. For a complete history of pushes, see [development commits](https://github.com/shamenra/MIST/commits/mist/development) or [all releases](https://github.com/shamenra/MIST/releases).

The historical Nexus Mods App changelog is preserved [separately](docs/upstream/CHANGELOG.md).

## Unreleased — development branch

### Added
- Read-only Steam library artwork resolution for installed-game cards, including hashed Steam artwork cache directories and bundled-image fallback (MIST-006-07A; locally built and verified for Cyberpunk 2077 and Oblivion Remastered).
- Dedicated M.I.S.T. README, roadmap, and supported-games tracker.

### Changed
- GitHub buttons point to the M.I.S.T. repository.
- Help → View changelog opens this M.I.S.T. changelog on GitHub.
- My Games → View roadmap opens the M.I.S.T. game and feature roadmap on GitHub.

### In progress
- Verify standalone AppImage OAuth/desktop integration on additional Linux environments.
- Improve original-game version coverage and tested game support.

## v0.6.0-dev.1 — 2026-11-05

- Corrected Linux AppImage desktop metadata and icon packaging so PupNet can produce a runnable M.I.S.T. AppImage.
- Preserved the independent M.I.S.T. branding and build/version information.
- First AppImage packaging milestone. [Release assets](https://github.com/shamenra/MIST/releases/tag/v0.6.0-dev.1).

## v0.6.0-dev — 2026-11-05

- First source prerelease of M.I.S.T.'s Linux fork and GitHub build workflow.
- Initial support for Oblivion Remastered alongside inherited supported-game integrations.
- Steam build identity, local depot manifests, verified original-file checks, and known public-version mappings.
- Receipt-based file reversal safety; removed inherited directory-cleanup behavior that could remove unrelated game directories.
- Removed inherited Nexus-targeted application telemetry registration while retaining Nexus account functionality.
- Introduced independent M.I.S.T. UI branding and upstream attribution.
- Initial AppImage workflow required follow-up packaging fixes in v0.6.0-dev.1.

**Note:** The changelog is maintained in source control and changes when contributors update this file. Git pushes do not automatically create changelog entries.
